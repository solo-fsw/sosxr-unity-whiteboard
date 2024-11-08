using System;
using UnityEngine;


/// <summary>
///     Based on Justin P. Barnett's tutorial on: https://www.youtube.com/watch?v=sHE5ubsP-E8&t=1803s
/// </summary>
public class Whiteboard : MonoBehaviour
{
    [SerializeField] private Vector2 m_textureSize = new(2048, 2048); // Create so that it updates to scale

    [SerializeField] private Renderer m_renderer;
    [Tooltip("If we want the eventual canvas to be throwable by XR, the MeshCollider needs to be on another object, which is not a child of the XR Grab Interactable (more precisely, not a child of the Rigidbody dealing with those components)")]
    [SerializeField] private Collider m_collider;

    public Vector2 TextureSize => m_textureSize;

    public Texture2D Texture { get; private set; }

    public Collider Collider
    {
        get => m_collider;
        private set => m_collider = value;
    }


    public void OnValidate()
    {
        if (m_renderer == null)
        {
            m_renderer = GetComponentInChildren<Renderer>();
        }

        if (m_collider == null)
        {
            m_collider = GetComponentInChildren<Collider>();
        }
    }


    private void Start()
    {
        SetTextureToRenderer();
    }


    [ContextMenu(nameof(SetTextureToRenderer))]
    public void SetTextureToRenderer()
    {
        if (m_renderer == null)
        {
            Debug.LogError("No renderer found! Cannot set texture.");

            return;
        }

        Texture = new Texture2D((int) TextureSize.x, (int) TextureSize.y);
        m_renderer.material.mainTexture = Texture; // Is the same as: _renderer.material.SetTexture("_MainTex", Texture);

        if (Math.Abs(transform.localScale.x - transform.localScale.y) > 0.01f ||
            Math.Abs(transform.localScale.x - transform.localScale.z) > 0.01f)
        {
            Debug.LogWarning("Whiteboard scale is not uniform. This may cause issues with the texture.");
        }
    }
}