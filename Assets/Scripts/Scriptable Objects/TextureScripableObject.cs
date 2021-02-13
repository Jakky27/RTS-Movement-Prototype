using UnityEngine;
using System.Collections;

[CreateAssetMenu(fileName = "Data", menuName = "TextureObject", order = 1)]
public class TextureScripableObject : ScriptableObject {
    public string objectName = "New Texture Object";
    public Texture2D mapTexture;
}
