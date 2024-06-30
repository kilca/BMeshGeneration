using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class SimpleScriptParser
{
     // Convert the root node and its children to a string in the custom language
    public static string ToCustomLanguage(Node rootNode)
    {
        return ToCustomLanguage(rootNode, 0);
    }

    private static string ToCustomLanguage(Node node, int indentLevel)
    {
        string indent = new string('>', indentLevel);
        string position = $"{node.transform.localPosition.x} {node.transform.localPosition.y} {node.transform.localPosition.z}";
        string result = $"{indent} {position} {node.size}";

        foreach (Transform childTransform in node.transform)
        {
            Node childNode = childTransform.GetComponent<Node>();
            if (childNode != null)
            {
                result += "\n" + ToCustomLanguage(childNode, indentLevel + 1);
            }
        }

        return result;
    }

    // Parse a string in the custom language to create a Node hierarchy
    public static Node ParseFromCustomLanguage(string code, Node rootNode)
    {
        //TODO
    }
    

}
