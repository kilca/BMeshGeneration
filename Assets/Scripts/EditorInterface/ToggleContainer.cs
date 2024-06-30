using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ToggleContainer : MonoBehaviour
{
    public Button[] buttons;

    private Button selectedButton;

    private void Start()
    {
        selectedButton = buttons[0];
    }

}
