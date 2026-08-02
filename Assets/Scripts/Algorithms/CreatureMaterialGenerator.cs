// ============================================================================
// Procedural Creature Material/Texture Generator
// ============================================================================
// The generated mesh has no UVs (convex-hull junctions between limbs have no
// consistent surface to unwrap), so texturing it through a UV-mapped texture
// would require solving mesh unwrapping for arbitrary organic topology --
// fragile and likely to look stretched/seamed. Triplanar mapping sidesteps
// that: it colors the mesh from world position + normal alone, so any mesh
// works with zero UV data. Most of the time the texture itself is generated
// too (a blotchy two-tone Perlin pattern with a random hue per creature), so
// nothing about material assignment needs to be authored by hand -- but see
// PhotoTextureChance for the alternative below.
using UnityEngine;

public static class CreatureMaterialGenerator
{
    private const string GeneratedMaterialName = "GeneratedCreatureSkin";
    private const string GeneratedWireframeMaterialName = "GeneratedWireframe";
    private const string GeneratedTextureName = "GeneratedSkinTexture";

    // Drop any number of real skin-pattern textures (zebra, cheetah, fur,
    // scales, ...) in Assets/Resources/Textures/Animal and they're picked up
    // automatically -- no per-texture wiring needed, same auto-resolve
    // philosophy as Resources/Node.prefab and Resources/Eye.prefab.
    private const string AnimalTexturesResourcesPath = "Textures/Animal";

    // How often GenerateMaterial picks one of those pre-made textures instead
    // of generating a procedural one. 0 = always procedural (the original
    // behavior), 1 = always a pre-made texture (falls back to procedural
    // anyway if the Resources folder above is empty).
    public static float PhotoTextureChance = 0.5f;

    private static Texture2D[] animalTextures;

    public static Material GenerateMaterial()
    {
        Texture2D texture = PickSkinTexture();

        Material material = new Material(Shader.Find("Custom/TriplanarCreature"));
        material.name = GeneratedMaterialName;
        material.mainTexture = texture;
        material.SetFloat("_TexScale", Random.Range(0.4f, 1.2f));
        material.SetFloat("_Glossiness", Random.Range(0.1f, 0.5f));

        return material;
    }

    // Replaces bmesh.normalMaterial with a freshly generated one, disposing the
    // previous material/texture only if it was one we generated ourselves --
    // a material the user assigned by hand is left untouched.
    public static void ApplyToBMesh(BMesh bmesh)
    {
        if (bmesh.normalMaterial != null && bmesh.normalMaterial.name == GeneratedMaterialName)
        {
            // Only the procedural texture is actually owned by this material --
            // a pre-made texture came from Resources.LoadAll and is a shared,
            // cached asset that must never be destroyed here, or every other
            // creature (and any future load) using the same texture breaks too.
            bool ownsTexture = bmesh.normalMaterial.mainTexture != null && bmesh.normalMaterial.mainTexture.name == GeneratedTextureName;

            // DestroyImmediate is only safe outside play mode; a build (or the
            // editor while playing) must use Destroy instead.
            if (Application.isPlaying)
            {
                if (ownsTexture) Object.Destroy(bmesh.normalMaterial.mainTexture);
                Object.Destroy(bmesh.normalMaterial);
            }
            else
            {
                if (ownsTexture) Object.DestroyImmediate(bmesh.normalMaterial.mainTexture);
                Object.DestroyImmediate(bmesh.normalMaterial);
            }
        }

        bmesh.normalMaterial = GenerateMaterial();

        // A BMesh added at runtime (see CreatureGeneratorUI.CreateCreature())
        // never gets a wireframeMaterial assigned by hand the way the old
        // MeshHandler prefab did -- selecting the Wireframe show mode left
        // BMesh.Update() assigning a null material, which rendered as an
        // unexpected default/blank mesh rather than an actual wireframe. Only
        // fill this in once; unlike the skin it doesn't need to change per
        // generation.
        if (bmesh.wireframeMaterial == null)
        {
            bmesh.wireframeMaterial = GenerateWireframeMaterial();
        }
    }

    private static Material GenerateWireframeMaterial()
    {
        Shader shader = Shader.Find("SuperSystems/Wireframe");
        if (shader == null)
        {
            return null;
        }

        Material material = new Material(shader);
        material.name = GeneratedWireframeMaterialName;
        material.SetColor("_WireColor", Color.cyan);
        material.SetColor("_BaseColor", new Color(0.05f, 0.05f, 0.05f));
        return material;
    }

    // Mixes the original procedural generator in with a chance of picking one
    // of the pre-made animal skin textures instead -- see PhotoTextureChance
    // and AnimalTexturesResourcesPath above.
    private static Texture2D PickSkinTexture()
    {
        Texture2D[] textures = GetAnimalTextures();
        if (textures.Length > 0 && Random.value < PhotoTextureChance)
        {
            return textures[Random.Range(0, textures.Length)];
        }

        return GenerateSkinTexture(256, 256);
    }

    // Loaded once and cached -- these are shared assets straight from
    // Resources, not owned by any one creature (see the ownsTexture check in
    // ApplyToBMesh, which relies on this never being confused with a
    // procedurally generated texture).
    private static Texture2D[] GetAnimalTextures()
    {
        if (animalTextures == null)
        {
            animalTextures = Resources.LoadAll<Texture2D>(AnimalTexturesResourcesPath);

            // The triplanar shader samples well outside the 0-1 UV range (world
            // position * _TexScale), so it needs Repeat -- force it here rather
            // than relying on each imported texture's own wrap mode setting
            // (Unity often defaults imports to Clamp, which would show as
            // stretched streaks at the triplanar seams).
            foreach (Texture2D texture in animalTextures)
            {
                texture.wrapMode = TextureWrapMode.Repeat;
            }
        }
        return animalTextures;
    }

    private static Texture2D GenerateSkinTexture(int width, int height)
    {
        Color baseColor = Random.ColorHSV(0f, 1f, 0.3f, 0.7f, 0.4f, 0.9f);
        Color spotColor = baseColor * Random.Range(0.5f, 0.8f);
        spotColor.a = 1f;

        float noiseScale = Random.Range(3f, 9f);
        float offsetX = Random.Range(0f, 1000f);
        float offsetY = Random.Range(0f, 1000f);

        Texture2D texture = new Texture2D(width, height) { name = GeneratedTextureName };
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float n = Mathf.PerlinNoise(x / (float)width * noiseScale + offsetX, y / (float)height * noiseScale + offsetY);
                texture.SetPixel(x, y, Color.Lerp(baseColor, spotColor, n));
            }
        }
        texture.Apply();
        texture.wrapMode = TextureWrapMode.Repeat;

        return texture;
    }
}
