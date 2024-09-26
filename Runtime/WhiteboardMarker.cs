using System;
using System.Collections;
using SOSXR.Extensions;
using UnityEngine;
using UnityEngine.Events;
using Random = UnityEngine.Random;


/// <summary>
///     Based on: Justin P. Barnett's whiteboard implementation
///     https://www.youtube.com/watch?v=sHE5ubsP-E8&t=1803s
/// </summary>
public class WhiteboardMarker : MonoBehaviour
{
    [Header("Whiteboard to draw on")]
    [Tooltip("If none found, will try to find one with TAG: WhiteboardCanvas")]
    [SerializeField] private Whiteboard m_whiteboard;

    [Header("Set to IgnoreRaycast layer!")]
    [Tooltip("Needs to be on Ignore Raycast layer!")]
    [SerializeField] private Transform m_drawPart;
    [Tooltip("Needs to be on Ignore Raycast layer!")]
    [SerializeField] private Transform m_end;

    [Header("Settings")]
    [SerializeField] private Vector2Int m_penSize = new(20, 20);

    [Header("Audio Player")]
    [SerializeField] private AudioSource m_audioSource;

    [Header("Fire Event")]
    [SerializeField] private UnityEvent m_startedDrawingOnCanvas;


    [Header("Drawing Settings")]
    [SerializeField] [Range(0f, 100f)] private float m_drawDensity = 75f;
    [SerializeField] private Color m_alternativeColor = Color.white;
    [Tooltip("How often does the entire draw calculation have to be done? This is in seconds")]
    [SerializeField] private float m_drawIntervalSeconds = 0.05f; // 0.05f seconds seems to work well
    [SerializeField] [Range(0.001f, 0.5f)] private float m_interpolationSpecificity = 0.1f;
    [SerializeField] private float m_amplitude = 0.1f;
    [SerializeField] private AnimationCurve m_amplitudeCurve = new(new Keyframe(0, 1), new Keyframe(1, 0));
    [SerializeField] private float m_duration = 0.01f;
    public static Action<float> DrawingPressure;


    private Color[] _colors; // Need to have an array equal to the length of #pixels we want to color in
    private Coroutine _drawCR;

    private float _drawPartHeight;
    private bool _hasStartedDrawingOnCanvas;
    private Vector2 _lastTouchPosition;
    private Quaternion _lastTouchRotation;
    private float _normalizedTouchDistance;
    private Vector2Int _previousPenSize;
    private Renderer _renderer;
    private Coroutine _saveTextureLocallyCR;
    private Coroutine _saveTextureOnServerCR;
    private RaycastHit _touch;
    private bool _touchedDuringLastFrame;
    private Vector2 _touchPosition;
    private int _x;
    private int _y;

    private static int _colorIndex;


    private void Awake()
    {
        _renderer = m_drawPart.GetComponent<Renderer>();

        _drawPartHeight = m_drawPart.localScale.y;

        WhiteboardFound();

        _touch = new RaycastHit();


        CreateColorArray();
    }


    private void Start()
    {
        StartDrawing();
    }


    private bool WhiteboardFound()
    {
        if (m_whiteboard != null)
        {
            return true;
        }

        m_whiteboard = FindObjectOfType<Whiteboard>();

        return m_whiteboard != null;
    }


    private void CreateColorArray()
    {
        _colors = new Color[m_penSize.x * m_penSize.y];

        for (var i = 0; i < _colors.Length; i++)
        {
            var chance = Random.Range(0f, 100f);

            _colors[i] = chance >= m_drawDensity ? m_alternativeColor : _renderer.material.color;
        }
    }


    /// <summary>
    ///     Called from XR grab OnSelectEnter
    /// </summary>
    [ContextMenu(nameof(StartDrawing))]
    public void StartDrawing()
    {
        if (_drawCR != null)
        {
            Debug.Log("Already drawing!");

            return;
        }

        _drawCR = StartCoroutine(DrawCR());
    }


    /// <summary>
    ///     Called from XR grab OnSelectExit
    /// </summary>
    [ContextMenu(nameof(StopDrawing))]
    public void StopDrawing()
    {
        if (_drawCR == null)
        {
            return;
        }

        StopCoroutine(_drawCR);
        _drawCR = null;
    }


    private IEnumerator DrawCR()
    {
        for (;;)
        {
            yield return new WaitForSeconds(m_drawIntervalSeconds);

            Draw();
        }
    }


    private void Draw()
    {
        if (!WhiteboardFound())
        {
            Debug.LogError("No whiteboard found! Cannot draw.");

            return;
        }

        if (IsHittingWhiteboard())
        {
            FireStartingDrawingOnCanvas();

            CalculateWhiteboardTouchPosition(_touch);

            if (OutOfBoundsOfWhiteboard(_x, _y))
            {
                StopPlayingAudio();

                return;
            }

            if (_touchedDuringLastFrame)
            {
                PlayAudio();

                InterpolateColorsFromLastFrame(_x, _y);

                SaveTexture();

                SendDrawingPressureEvent();
            }

            SetLastFrameValues(_x, _y);

            return;
        }

        StopTouching();
    }


    private void PlayAudio()
    {
        if (m_audioSource.isPlaying)
        {
            return;
        }

        m_audioSource.Play();
    }


    private void StopPlayingAudio()
    {
        if (!m_audioSource.isPlaying)
        {
            return;
        }

        m_audioSource.Stop();
    }


    private void SendDrawingPressureEvent()
    {
        var oldDistance = new Vector2(0, _drawPartHeight);
        var newDistance = new Vector2(0, 1);
        _normalizedTouchDistance = _touch.distance.RemapValue(oldDistance, newDistance);
        var amplitude = m_amplitude * m_amplitudeCurve.Evaluate(_normalizedTouchDistance);

        DrawingPressure?.Invoke(amplitude);
    }


    private bool IsHittingWhiteboard()
    {
        return Physics.Raycast(m_end.position, transform.up, out _touch, _drawPartHeight) && _touch.transform == m_whiteboard.Collider.transform;
    }


    private void FireStartingDrawingOnCanvas()
    {
        m_startedDrawingOnCanvas?.Invoke();
    }


    private void CalculateWhiteboardTouchPosition(RaycastHit touch)
    {
        _touchPosition = new Vector2(touch.textureCoord.x, touch.textureCoord.y);

        var size = m_whiteboard.TextureSize;

        _x = (int) (_touchPosition.x * size.x - m_penSize.x / 2);
        _y = (int) (_touchPosition.y * size.y - m_penSize.y / 2);
    }


    private bool OutOfBoundsOfWhiteboard(int x, int y)
    {
        return y < 0 || y + m_penSize.y > m_whiteboard.TextureSize.y || x < 0 || x + m_penSize.x > m_whiteboard.TextureSize.x;
    }


    private void InterpolateColorsFromLastFrame(int x, int y)
    {
        for (var f = 0.01f; f < 1.00f; f += m_interpolationSpecificity)
        {
            var lerpX = (int) Mathf.Lerp(_lastTouchPosition.x, x, f);
            var lerpY = (int) Mathf.Lerp(_lastTouchPosition.y, y, f);

            SetPixels(lerpX, lerpY);
        }
    }


    private void SetPixels(int lerpX, int lerpY)
    {
        if (m_whiteboard == null)
        {
            Debug.LogError("Whiteboard is null in SetPixels!");

            return;
        }

        if (m_whiteboard.Texture == null)
        {
            Debug.LogError("Whiteboard texture is null in SetPixels!");

            return;
        }

        if (_colors == null || _colors.Length == 0)
        {
            Debug.LogError("Colors array is null or empty in SetPixels!");

            return;
        }

        m_whiteboard.Texture.SetPixels(lerpX, lerpY, m_penSize.x, m_penSize.y, _colors, 0);
    }


    private void SaveTexture()
    {
        m_whiteboard.Texture.Apply();
    }


    private void SetLastFrameValues(int x, int y)
    {
        _lastTouchPosition = new Vector2(x, y);
        _touchedDuringLastFrame = true;
    }


    private void StopTouching()
    {
        if (m_audioSource.isPlaying)
        {
            m_audioSource.Stop();
        }

        _touchedDuringLastFrame = false;

        if (_saveTextureOnServerCR != null)
        {
            StopCoroutine(_saveTextureOnServerCR);
        }

        _saveTextureOnServerCR = null;

        if (_saveTextureLocallyCR != null)
        {
            StopCoroutine(_saveTextureLocallyCR);
        }

        _saveTextureLocallyCR = null;
    }


    private void OnDisable()
    {
        StopAllCoroutines();
    }
}