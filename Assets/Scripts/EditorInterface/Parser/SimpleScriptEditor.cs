using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
public class SimpleScriptEditor : MonoBehaviour
{

    public InputField inputField;

    public Node node;

    // Start is called before the first frame update
    void Start()
    {
        string text = "# this is a comment"+"\n"+"# we represent the node like this:"+"\n"+"# > x y z size"+"\n";
        text += SimpleScriptParser.ToCustomLanguage(node);
        inputField.text = text;
    }

    public void OnEditField(string text){
        Clear();
        Debug.Log(text);
        SimpleScriptParser.ParseFromCustomLanguage(text,node);
    }

    public void Clear()
    {
        foreach (Transform child in node.transform)
        {
            DestroyImmediate(child.gameObject);
        }
    }

    //TODO Add parser in other direction

}
