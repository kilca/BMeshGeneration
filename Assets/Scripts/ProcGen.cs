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
    public enum GenType { Biped,Creature};
    public GenType genType;

    public int seed;

    [Header("References")]
    public GameObject nodePrefab;

    public GameObject lowBody;
    public GameObject topBody;

    private Vector3 randomVector(Vector2 x, Vector2 y, Vector2 z)
    {
        return new Vector3(Random.Range(x.x, x.y), Random.Range(y.x, y.y),Random.Range(z.x,z.y));
    }

    public void Clear()
    {
        if (lowBody != null)
        {
            DestroyImmediate(lowBody);
        }
    }

    public void Generate()
    {
        seed = (int)System.DateTime.Now.Ticks;
        Random.InitState(seed);
        GenerateBiped();
    }

    void GenerateLegs()
    {
        lowBody = Instantiate(nodePrefab, new Vector3(0, 2, 0), Quaternion.identity, transform);

        //-----Leg Generation

        Vector2 minMax = new Vector2(-0.5f, 0.5f);

        Vector3 rVec = randomVector(minMax, minMax, minMax);
        GameObject upLeg = Instantiate(nodePrefab, lowBody.transform);
        upLeg.transform.localPosition = new Vector3(0, -2f, -1.5f) + rVec;

        rVec = randomVector(minMax, minMax, minMax);
        GameObject lowLeg = Instantiate(nodePrefab, upLeg.transform);
        lowLeg.transform.localPosition = new Vector3(0, -2f, 0) + rVec;

        rVec = randomVector(new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
        GameObject foot = Instantiate(nodePrefab, lowLeg.transform);
        foot.transform.localPosition = new Vector3(1.0f, 0, 0) + rVec;
        //----- Right Leg Generation

        GameObject upLegR = Instantiate(upLeg, lowBody.transform);
        upLegR.transform.localPosition = Vector3.Scale(upLegR.transform.localPosition, new Vector3(1, 1, -1));
        foreach (Transform t in upLegR.transform)
        {
            t.localPosition = Vector3.Scale(t.localPosition, new Vector3(1, 1, -1));
        }
    }

    void GenerateUpBody()
    {
        Vector2 minMax = new Vector2(-0.5f, 0.5f);

        Vector3 rVec = randomVector(minMax, minMax, Vector2.zero);
        GameObject midBody = Instantiate(nodePrefab, lowBody.transform);
        midBody.transform.localPosition = new Vector3(0, 2, 0) + rVec;

        rVec = randomVector(minMax, minMax, Vector2.zero);
        topBody = Instantiate(nodePrefab, midBody.transform);
        topBody.transform.localPosition = new Vector3(0, 2, 0) + rVec;

    }

    //Look like foot
    void GenerateArms()
    {
        
        //-----Arm Generation

        Vector2 minMax = new Vector2(-0.5f, 0.5f);

        Vector3 rVec = randomVector(minMax, minMax, minMax);
        GameObject upLeg = Instantiate(nodePrefab, topBody.transform);
        upLeg.transform.localPosition = new Vector3(0, -2f, -2.5f) + rVec;

        rVec = randomVector(minMax, minMax, minMax);
        GameObject lowLeg = Instantiate(nodePrefab, upLeg.transform);
        lowLeg.transform.localPosition = new Vector3(0, -2f, 0) + rVec;

        GameObject upLegR = Instantiate(upLeg, topBody.transform);
        upLegR.transform.localPosition = Vector3.Scale(upLegR.transform.localPosition, new Vector3(1, 1, -1));
        foreach (Transform t in upLegR.transform)
        {
            t.localPosition = Vector3.Scale(t.localPosition, new Vector3(1, 1, -1));
        }

    }

    void SetSize(GameObject g, float min, float max)
    {
        Node n = g.GetComponent<Node>();
        n.size = Random.Range(min, max);
    }

    void GenerateHead()
    {
        Vector2 minMax = new Vector2(-0.2f, 0.2f);

        Vector3 rVec = randomVector(minMax, minMax, minMax);
        GameObject neck = Instantiate(nodePrefab, topBody.transform);
        neck.transform.localPosition = new Vector3(0, 1.0f, 0f) + rVec;
        SetSize(neck, 0.2f, 0.6f);
        neck.name = "neck";


        rVec = randomVector(minMax, minMax, minMax);
        GameObject head = Instantiate(nodePrefab, neck.transform);
        head.name = "head";
        head.transform.localPosition = new Vector3(0, 1.0f, 0f) + rVec;

        GameObject headFront = Instantiate(nodePrefab, head.transform);
        headFront.name = "headFront";
        headFront.transform.localPosition = new Vector3(1.0f, 0.0f, 0f);
        SetSize(headFront, 0.3f, 0.8f);

        GameObject headBack = Instantiate(nodePrefab, head.transform);
        headBack.name = "headFront";
        headBack.transform.localPosition = new Vector3(-1.0f, 0.0f, 0f);
        SetSize(headBack, 0.3f, 0.8f);
    }

    public void GenerateBiped()
    {
        Clear();
        GenerateLegs();
        GenerateUpBody();
        GenerateArms();
        GenerateHead();

    }

}
