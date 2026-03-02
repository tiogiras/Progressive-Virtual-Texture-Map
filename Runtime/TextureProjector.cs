using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Tiogiras.PVTM
{
    /// <summary>
    /// 
    /// </summary>
    public class TextureProjection : MonoBehaviour
    {
        /// <summary>
        ///     Floor-Eye Distance for the average human based on
        ///     2012 ANTHROPOMETRIC SURVEY OF U.S. ARMY PERSONNEL: METHODS AND SUMMARY STATISTICS
        /// </summary>
        private const float _ViewDistance = 1.6f;

        /// <summary> Stores the tangent of one degree </summary>
        private const float _TanOneDeg = 0.01745f;

        /// <summary> Stores the shader property id for the base texture map of the base material </summary>
        private static readonly int s_baseMap = Shader.PropertyToID("_BaseMap");

        /// <summary> Simple matrix to simplify accessing the related pages </summary>
        private static readonly Vector2Int[] s_childOffsets =
        {
            new(0, 0),
            new(1, 0),
            new(0, 1),
            new(1, 1)
        };

        [Header("References")] 
        [SerializeField] [Tooltip("Reference to the camera rendering the projection target")]
        private Camera _camera;
        
        [SerializeField] [Tooltip("Reference to the parent reiceiving the recreation planes")]
        private Transform _recreation;
        
        [SerializeField] [Tooltip("Store the material used by the recreation planes")]
        private Material _recreationMaterial;

        [Header("Settings")] 
        [SerializeField] [Range(1, 5)] [Tooltip("Defines the amount of mip levels the texture should be divided into")]
        private int _mipCount = 3;

        [SerializeField] [Range(1, 94)] [Tooltip("Defines the target pixel per degree (used to calculate the target resolution)")] 
        private float _ppdTarget = 60;
        
        [SerializeField] [Tooltip("Defines the size of the recreation in physical meter (used to calculate the target resolution)")]
        private float _size = 10;

        [SerializeField] [Tooltip("Defines the wait period between each page render in seconds")]
        private float _waitPeriod = .5f;

        /// <summary> Stores all currently enabled pages </summary>
        private readonly List<Vector3Int> _enabledPages = new();
        
        /// <summary>
        ///     Holds a reference between the mip level, the page coordinates and the <see cref="MeshRenderer"/>
        ///     of the recreation plane
        /// </summary>
        private readonly Dictionary<int, Dictionary<Vector2Int, MeshRenderer>> _recreationRenderers = new();

        /// <summary>
        ///     Holds a reference between the mip level, the page coordinates and the virtual
        ///     <see cref="RenderTexture"/> storing the rendered page
        /// </summary>
        private readonly Dictionary<int, Dictionary<Vector2Int, RenderTexture>> _rts = new();

        /// <summary> Stores a reference to the camera transform </summary>
        private Transform _camTransform;
        
        /// <summary>
        ///     Stores an empty <see cref="MaterialPropertyBlock"/> to later read those from the recreation plane's
        ///     materials
        /// </summary>
        private MaterialPropertyBlock _mpb;

        /// <summary> Returns the amount of mip levels the texture should be divided into </summary>
        public int mipCount => _mipCount;
        
        /// <summary> Returns the transform of the camera </summary>
        private Transform _cameraTransform => _camTransform ??= _camera.transform;

        private void Awake()
        {
            _mpb = new MaterialPropertyBlock();

            _camera.enabled = false;
            SetupRecreation();
            SetupRTs();
        }

        /// <summary> Calculate the number of pages of any given mip level </summary>
        /// <param name="level"> The mip level to get the pages for </param>
        /// <returns> The amount of pages on the given mip level </returns>
        public static int PageCount(int level)
        {
            if (level == 0)
                return 1;

            var count = 1;

            for (var i = 0; i < level; i++) 
                count *= 4;

            return count;
        }

        
        /// <summary>
        ///     Calculate the resolution for all pages given the settings specified on this <see cref="TextureProjection"/>
        /// </summary>
        /// <returns> The resolution of the smallest page / page on the highest mip level </returns>
        public float PageResolution()
        {
            return Mathf.Ceil(CalculateRequiredResolution() / Mathf.Sqrt(PageCount(_mipCount - 1)));
        }

        /// <summary> Calculates required resolution to display the projection with the targeted PPD on the given size </summary>
        public float CalculateRequiredResolution()
        {
            return Mathf.Ceil(_ppdTarget / (_TanOneDeg * _ViewDistance) * _size);
        }

        /// <summary> Start a full projection of all mip levels </summary>
        [ContextMenu("Start Full Projection")]
        public void StartFullProjection()
        {
            StartCoroutine(ProjectFullMapIE());
        }
        
        /// <summary> Project all pages of all mip levels onto their corresponding render textures </summary>
        private IEnumerator ProjectFullMapIE()
        {
            _enabledPages.Clear();

            foreach (var valuePair in _recreationRenderers.SelectMany(keyValuePair => keyValuePair.Value))
                valuePair.Value.enabled = false;

            for (var i = 0; i < _mipCount; i++) 
                yield return ProjectPages(i);
        }
        
        /// <summary>
        ///     Project all pages of the given mip level onto their corresponding <see cref="RenderTexture"/>.
        ///     Does not project more than one page per set <see cref="_waitPeriod"/>
        /// </summary>
        /// <param name="mipLevel"> The mip level the pages should be projected for </param>
        private IEnumerator ProjectPages(int mipLevel)
        {
            var pageCount = PageCount(mipLevel);
            var rowCount = (int)Mathf.Sqrt(pageCount);

            var projectionSize = SetCameraProjectionSize(rowCount);

            for (var x = 0; x < rowCount; x++)
            {
                _cameraTransform.localPosition =
                    _cameraTransform.localPosition.CopyWithX(-0.5f + projectionSize + x * 2 * projectionSize);

                for (var y = 0; y < rowCount; y++)
                {
                    _cameraTransform.localPosition =
                        _cameraTransform.localPosition.CopyWithY(-0.5f + projectionSize + y * 2 * projectionSize);

                    _camera.targetTexture = _rts[mipLevel][new Vector2Int(x, y)];
                    _camera.aspect = 1;

                    _camera.enabled = true;
                    _camera.Render();
                    _camera.enabled = false;
                    _camera.targetTexture = null;

                    var id = new Vector3Int(mipLevel, x, y);

                    _recreationRenderers[mipLevel][new Vector2Int(x, y)].enabled = true;
                    _enabledPages.Add(id);

                    TryDisableParents(mipLevel, x, y);

                    yield return new WaitForSeconds(.5f);
                }
            }
        }

        /// <summary> Creates all required <see cref="RenderTexture"/>s and map them to their mip level and corresponding page coordinates </summary>
        private void SetupRTs()
        {
            for (var mipLevel = 0; mipLevel < _mipCount; mipLevel++)
            {
                _rts.Add(mipLevel, new Dictionary<Vector2Int, RenderTexture>());

                var pageCount = PageCount(mipLevel);
                var rowCount = (int)Mathf.Sqrt(pageCount);

                for (var x = 0; x < rowCount; x++)
                for (var y = 0; y < rowCount; y++)
                {
                    var rt = CreateRenderTexture();
                    _rts[mipLevel].Add(new Vector2Int(x, y), rt);

                    var recreation = _recreationRenderers[mipLevel][new Vector2Int(x, y)];

                    recreation.GetPropertyBlock(_mpb);
                    _mpb.SetTexture(s_baseMap, rt);
                    recreation.SetPropertyBlock(_mpb);
                }
            }
        }

        /// <summary> Create an in memory <see cref="RenderTexture"/> fit to hold a projected page </summary>
        private RenderTexture CreateRenderTexture()
        {
            var pageRes = (int)PageResolution();

            var rt = new RenderTexture(pageRes, pageRes, GraphicsFormat.R8G8B8A8_SRGB, GraphicsFormat.D24_UNorm_S8_UInt)
            {
                useMipMap = false,
                autoGenerateMips = false
            };

            rt.Create();
            return rt;
        }

        /// <summary>
        ///     Create all required planes that will receive the projected <see cref="RenderTexture"/> and populate the
        ///     <see cref="_recreation"/> gameObject with them
        /// </summary>
        private void SetupRecreation()
        {
            for (var mipLevel = 0; mipLevel < _mipCount; mipLevel++)
            {
                _recreationRenderers.Add(mipLevel, new Dictionary<Vector2Int, MeshRenderer>());

                var pageCount = PageCount(mipLevel);
                var rowCount = (int)Mathf.Sqrt(pageCount);

                var projectionSize = SetCameraProjectionSize(rowCount);

                for (var x = 0; x < rowCount; x++)
                for (var y = 0; y < rowCount; y++)
                    _recreationRenderers[mipLevel].Add(new Vector2Int(x, y),
                        CreateRecreationRenderer(mipLevel, rowCount, x, y, projectionSize));
            }
        }

        /// <summary>
        ///     Create and add a gameObject fit to receive the projected <see cref="RenderTexture"/> of a page.
        ///     Creates parent gameObjects based on the mip level.
        /// </summary>
        /// <param name="mipLevel"> Defines the mip level of the corresponding page </param>
        /// <param name="rowCount"> Defines the amount of pages per row </param>
        /// <param name="x"> Defines the x coordinate of the corresponding page in its mip level </param>
        /// <param name="y"> Defines the y coordinate of the corresponding page in its mip level </param>
        /// <param name="projectionSize"> Stores the orthogonal projection size of the rendering <see cref="Camera"/> </param>
        private MeshRenderer CreateRecreationRenderer(int mipLevel, int rowCount, int x, int y, float projectionSize)
        {
            var parentName = $"Mip Level {mipLevel}";
            var parent = _recreation.Find(parentName);

            if (parent == null)
            {
                var go = new GameObject(parentName);
                go.transform.SetParent(_recreation);
                go.transform.localPosition = new Vector3(0, mipLevel * .0001f, 0);
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;

                parent = go.transform;
            }

            var page = GameObject.CreatePrimitive(PrimitiveType.Plane);
            page.name = $"Page {x * rowCount + y + 1}";
            page.transform.SetParent(parent);
            page.transform.localPosition = new Vector3(-0.5f + projectionSize + x * 2 * projectionSize, 0,
                -0.5f + projectionSize + y * 2 * projectionSize);
            page.transform.localRotation = Quaternion.Euler(new Vector3(0, 180, 0));
            page.transform.localScale = Vector3.one / rowCount / 10;

            Destroy(page.GetComponent<Collider>());

            var mr = page.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.sharedMaterial = _recreationMaterial;

            return mr;
        }

        /// <summary> Check if the recreation plane with the given id is currently enabled </summary>
        /// <param name="id"> ID of the targeted recreation plane (mip level, x, y) </param>
        private bool IsEnabled(Vector3Int id)
        {
            return _enabledPages.Contains(id);
        }

        /// <summary> Disable the recreation plane with the given id </summary>
        /// <param name="id"> ID of the targeted recreation plane (mip level, x, y) </param>
        private void DisablePage(Vector3Int id)
        {
            _recreationRenderers[id.x][new Vector2Int(id.y, id.z)].enabled = false;
            _enabledPages.Remove(id);
        }

        /// <summary>
        ///     Try to disable the parents of the page with the given id.
        ///     A parent will be disabled if all children are enabled, meaning the parent plane is fully obstructed
        /// </summary>
        /// <param name="mip"> ID part (mip level) </param>
        /// <param name="x"> ID part (x) </param>
        /// <param name="y"> ID part (y) </param>
        private void TryDisableParents(int mip, int x, int y)
        {
            if (mip <= 0) 
                return;

            var parent = new Vector3Int(mip - 1, x / 2, y / 2);
            
            if (!IsEnabled(parent)) 
                return;
            
            var childMip = parent.x + 1;
            var baseX = parent.y * 2;
            var baseY = parent.z * 2;

            foreach (var o in s_childOffsets)
            {
                var child = new Vector3Int(childMip, baseX + o.x, baseY + o.y);
                
                if (!IsEnabled(child))
                    return;
            }
            
            DisablePage(parent);
            
            TryDisableParents(parent.x, parent.y, parent.z);
        }
        
        /// <summary>
        ///     Calculate and apply the orthographic projection size for the <see cref="Camera"/> rendering the projection
        /// </summary>
        /// <param name="pageCount"> Defines in how many pages the full projection viewport must be split </param>
        private float SetCameraProjectionSize(int pageCount)
        {
            _camera.orthographicSize = 0.5f / pageCount;

            return 0.5f / pageCount;
        }
    }
}