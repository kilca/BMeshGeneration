using System.Collections.Generic;
using UnityEngine;

// Generates and manages a grid of independent creatures at once -- see
// CreatureGeneratorUI's Single/Multiple mode toggle. Each is a fully normal
// CreatureGenerator, just laid out in a grid instead of one creature
// occupying the scene. Node editing/selection (NodeEditController) already
// works on any Node found in the scene regardless of which creature it
// belongs to, so no special interaction code is needed here.
public class CreatureBatchGenerator : MonoBehaviour
{
    [Tooltip("How many creatures to generate at once.")]
    [Range(2, 16)]
    public int count = 6;

    [Tooltip("Distance between creatures in the grid.")]
    public float spacing = 5f;

    public IReadOnlyList<CreatureGenerator> Creatures => creatures;
    private readonly List<CreatureGenerator> creatures = new List<CreatureGenerator>();

    // Settings applied uniformly to every creature in the batch -- passed in
    // rather than read from a single CreatureGenerator field, since there's
    // no one "the" creature to read them from in Multiple mode.
    public void GenerateBatch(int complexity, bool addEyes, CreatureGenerator.AnimationMode animationMode, GameObject nodePrefab, GameObject eyePrefab)
    {
        ClearBatch();

        int columns = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(count)));
        Vector3 origin = ComputeOrigin(columns);

        for (int i = 0; i < count; i++)
        {
            int column = i % columns;
            int row = i / columns;
            Vector3 position = origin + new Vector3(column * spacing, 0f, row * spacing);

            GameObject go = new GameObject($"Creature_{i}");
            go.transform.position = position;
            go.AddComponent<BMesh>();
            CreatureGenerator generator = go.AddComponent<CreatureGenerator>();
            generator.complexity = complexity;
            generator.addEyes = addEyes;
            generator.animationMode = animationMode;
            generator.nodePrefab = nodePrefab;
            generator.eyePrefab = eyePrefab;
            generator.Generate();

            creatures.Add(generator);
        }
    }

    public void ClearBatch()
    {
        foreach (CreatureGenerator generator in creatures)
        {
            if (generator != null)
            {
                Object.DestroyImmediate(generator.gameObject);
            }
        }
        creatures.Clear();
    }

    // Combined bounds across every creature in the batch -- used to auto-frame
    // the camera on the whole grid (see OrbitCamera.Frame).
    public Bounds ComputeBounds()
    {
        Bounds bounds = new Bounds(transform.position, Vector3.one);
        bool first = true;
        foreach (CreatureGenerator generator in creatures)
        {
            if (generator == null || generator.body == null)
            {
                continue;
            }

            Bounds creatureBounds = OrbitCamera.ComputeNodeBounds(generator.body);
            if (first)
            {
                bounds = creatureBounds;
                first = false;
            }
            else
            {
                bounds.Encapsulate(creatureBounds);
            }
        }
        return bounds;
    }

    // Roughly centers the grid a fixed distance in front of the camera (or
    // the world origin if there's no camera yet).
    private Vector3 ComputeOrigin(int columns)
    {
        int rows = Mathf.CeilToInt((float)count / columns);
        Vector3 gridCenterOffset = new Vector3((columns - 1) * spacing * 0.5f, 0f, (rows - 1) * spacing * 0.5f);

        Vector3 basePosition = Camera.main != null
            ? Camera.main.transform.position + Camera.main.transform.forward * (spacing * columns * 0.6f)
            : Vector3.zero;

        return basePosition - gridCenterOffset;
    }
}
