using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BMeshInterfaceEditor : MonoBehaviour
{
    public BMesh bMesh;

    public void OnSelectEdit(){
        bMesh.showMode = BMesh.ShowMode.Wireframe;
    }

    public void OnSelectView(){
        bMesh.showMode = BMesh.ShowMode.Mesh;
    }
    
    public void OnSelectText(){
        bMesh.showMode = BMesh.ShowMode.Wireframe;
    }
}
