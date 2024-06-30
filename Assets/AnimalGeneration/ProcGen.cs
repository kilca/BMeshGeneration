using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
[CustomEditor(typeof(ProcGen))]
public class ProcGenEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        ProcGen bm = (ProcGen)target;

        if (GUILayout.Button("Generate"))
        {
            bm.Generate();
        }
        if (GUILayout.Button("Clear"))
        {
            bm.Clear();
        }

    }
}
public class ProcGen : MonoBehaviour
{
    public enum GenNodeType
    {
        Root = 0,
        Member = 1, // eg : Legs or arms : will divide in the bottom
        Spine = 2, // eg : Spine or tail: will continue the member 
        Head = 3, // eg : Head : will end with larger size
        Nothing = 4, // Nothing
    };

    public List<List<int>> probabilities = new List<List<int>> {
        new List<int> { // Root
            0, //root
            0, //member
            1, //spine
            0, //head
            2,  //nothing
        },
        new List<int> { // Member
            0, //root
            2, //member
            0, //spine
            0, //head
            3  //nothing
        },
        new List<int> { // Spine
            0, //root
            2, //member
            2, //spine
            0, //head
            1  //nothing
        },
        new List<int> { // Head
            0, //root
            0, //member
            0, //spine
            0, //head
            1  //nothing
        }
    };

    public int seed;

    [Header("References")]
    public GameObject nodePrefab;

    public GameObject headPrefab;

    public float scaleFactor = 1.0f;
    public float dispertionFactor = 2.0f;

    private Vector3 randomVector(Vector2 x, Vector2 y, Vector2 z)
    {
        return new Vector3(Random.Range(x.x, x.y), Random.Range(y.x, y.y), Random.Range(z.x, z.y));
    }

    public void Clear()
    {
        foreach (Transform child in transform)
        {
            DestroyImmediate(child.gameObject);
        }
    }

    private void CreateRoot()
    {
        GameObject g = Instantiate(nodePrefab, Vector3.zero, Quaternion.identity, transform);
        g.name = "Root";
        g.GetComponent<Node>().size = Random.RandomRange(1.0f, 2.0f);
        GenerateChild(g.transform, GenNodeType.Root);
    }

    private void CreateMember(Transform parent)
    {
        Vector2 minMax = new Vector2(-2f * dispertionFactor, 2f * dispertionFactor); // Example range, you can adjust as needed
        Vector3 rVec = GenerateValidPosition(parent, minMax);
        GameObject g = Instantiate(nodePrefab, parent.position + rVec, Quaternion.identity, parent);
        g.transform.localPosition += rVec;
        g.GetComponent<Node>().size = Random.RandomRange(0.5f, 1.0f) * scaleFactor;
        g.name = "Member";
        GenerateChild(g.transform, GenNodeType.Member);
    }

    private void CreateSpine(Transform parent)
    {
        Vector2 minMax = new Vector2(-0.5f * dispertionFactor, 0.5f * dispertionFactor);
        Vector3 rVec = GenerateValidPosition(parent, minMax);
        Vector3 parentVec = parent.position - parent.parent.position;
        GameObject g = Instantiate(nodePrefab, parent.position + (rVec + parentVec)/2, Quaternion.identity, parent);
        g.transform.localPosition += rVec;
        g.GetComponent<Node>().size = Random.RandomRange(0.7f, 2.0f) * scaleFactor;
        g.name = "Spine";
        GenerateChild(g.transform, GenNodeType.Spine);
    }

    private void CreateHead(Transform parent)
    {
        Vector2 minMax = new Vector2(-2f * dispertionFactor, 2f * dispertionFactor); // Example range, you can adjust as needed
        Vector3 rVec = GenerateValidPosition(parent, minMax);
        GameObject g = Instantiate(headPrefab, parent.position + rVec, Quaternion.identity, parent);
        g.GetComponent<Node>().size = Random.RandomRange(0.8f, 1.3f) * scaleFactor;
        g.transform.localPosition += rVec;
        g.name = "Head";
    }

    private void GenerateChild(Transform parent, GenNodeType parentType)
    {
        int minProb = parentType == GenNodeType.Root ? 1 : 0;
        int maxProb = Mathf.Max(1, 7 - parent.hierarchyCount);
        int numChildren = Random.Range(minProb, maxProb);

        for (int i = 0; i < numChildren; i++)
        {
            GenNodeType childType = GetRandomNodeType(parentType);
            switch (childType)
            {
                case GenNodeType.Root:
                    CreateRoot();
                    break;
                case GenNodeType.Member:
                    CreateMember(parent);
                    break;
                case GenNodeType.Spine:
                    CreateSpine(parent);
                    break;
                case GenNodeType.Head:
                    CreateHead(parent);
                    break;
                case GenNodeType.Nothing:
                default:
                    break;
            }
        }
    }

    private GenNodeType GetRandomNodeType(GenNodeType parentType)
    {
        List<int> probs = probabilities[(int)parentType];
        int totalProb = 0;
        foreach (int prob in probs)
        {
            totalProb += prob;
        }

        int randomPoint = Random.Range(0, totalProb);
        for (int i = 0; i < probs.Count; i++)
        {
            if (randomPoint < probs[i])
            {
                return (GenNodeType)i;
            }
            randomPoint -= probs[i];
        }
        return GenNodeType.Nothing;
    }

    public float GetAngleBetweenVectors(Vector3 a, Vector3 b)
    {
        float dotProduct = Vector3.Dot(a.normalized, b.normalized);
        float angle = Mathf.Acos(dotProduct) * Mathf.Rad2Deg;
        return angle;
    }

    private Vector3 GenerateValidPosition(Transform parent, Vector2 minMax)
    {
        Vector3 rVec;
        bool positionValid;
        int attempts = 0;

        do
        {
            rVec = randomVector(minMax, minMax, minMax);
            positionValid = true;
            foreach (Transform sibling in parent)
            {
                Vector3 directionToNewPos = (parent.position + rVec) - sibling.position;
                Vector3 siblingDirection = sibling.position - parent.position;
                float angle = Vector3.Angle(siblingDirection, directionToNewPos);

                if (angle < 30.0f) // Example angle threshold, adjust as needed
                {
                    positionValid = false;
                    break;
                }
            }
            attempts++;
            if (attempts >= 10){
                Debug.Log("Max attempt reached");
            }
        } while (!positionValid && attempts < 10);

        return rVec;
    }

    private void FindSpinesWithoutChildren(Transform parent, List<Transform> spinesWithoutChildren)
    {
        foreach (Transform child in parent)
        {
            if (child.name == "Spine" && child.childCount == 0)
            {
                spinesWithoutChildren.Add(child);
            }

            FindSpinesWithoutChildren(child, spinesWithoutChildren);
        }
    }
    private void AddHeadToFirstSpineWithoutChildren()
    {
        List<Transform> spinesWithoutChildren = new List<Transform>();

        foreach (Transform child in transform)
        {
            FindSpinesWithoutChildren(child, spinesWithoutChildren);
        }

        if (spinesWithoutChildren.Count > 0){
            CreateHead(spinesWithoutChildren[0]);
        }
    }

    public void Generate()
    {
        Clear();
        CreateRoot();
        AddHeadToFirstSpineWithoutChildren();
    }

}
