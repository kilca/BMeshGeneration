using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class ApplicationSettings
{
    #if UNITY_EDITOR
        public static bool IsUnityEditor = true;
    #else
        public static bool IsUnityEditor = false;
    #endif
    #if PLATFORM_STANDALONE_WIN
        public static bool IsPlatformStandaloneWin = true;
    #else
        public static bool IsPlatformStandaloneWin = false;
    #endif
}
