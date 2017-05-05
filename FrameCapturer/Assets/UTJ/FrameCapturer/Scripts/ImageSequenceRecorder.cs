using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;


namespace UTJ.FrameCapturer
{

    [AddComponentMenu("UTJ/FrameCapturer/Image Sequence Recorder")]
    [RequireComponent(typeof(Camera))]
    public class ImageSequenceRecorder : MonoBehaviour
    {
        public enum CaptureTarget
        {
            FrameBuffer,
            RenderTexture,
        }
        
        [Serializable]
        public struct FrameBufferConponents
        {
            public bool frameBuffer;
            public bool GBuffer;
            public bool  albedo;
            public bool  occlusion;
            public bool  specular;
            public bool  smoothness;
            public bool  normal;
            public bool  emission;
            public bool  depth;

            public static FrameBufferConponents default_value
            {
                get
                {
                    var ret = new FrameBufferConponents
                    {
                        frameBuffer = true,
                        GBuffer = false,
                        albedo = true,
                        occlusion = true,
                        specular = true,
                        smoothness = true,
                        normal = true,
                        emission = true,
                        depth = true,
                    };
                    return ret;
                }
            }
        }


        [SerializeField] public ImageSequenceRecorderContext.Type m_format = ImageSequenceRecorderContext.Type.Exr;
        [SerializeField] public DataPath m_outputDir = new DataPath(DataPath.Root.Current, "");
        [SerializeField] public CaptureTarget m_captureTarget = CaptureTarget.FrameBuffer;
        [SerializeField] public FrameBufferConponents m_fbComponents = FrameBufferConponents.default_value;
        [SerializeField] public RenderTexture[] m_targetRT;
        [SerializeField] public bool m_fixDeltaTime = true;
        [SerializeField] public int m_targetFramerate = 30;
        [SerializeField] public int m_startFrame = 0;
        [SerializeField] public int m_endFrame = 0;

        [SerializeField] protected ImageSequenceRecorderContext m_ctx;
        [SerializeField] protected Shader m_shCopy;
        protected Material m_matCopy;
        protected Mesh m_quad;
        protected CommandBuffer m_cbCopyFB;
        protected CommandBuffer m_cbCopyGB;
        protected CommandBuffer m_cbCopyRT;
        protected RenderTexture m_rtFB;
        protected RenderTexture[] m_rtGB;
        protected RenderTexture[] m_rtScratch;


        public ImageSequenceRecorderContext.Type format
        {
            get { return m_format; }
            set { m_format = value; }
        }
        public bool isRecording
        {
            get { return false; } // todo
        }


        public void Export()
        {

        }


        public void BeginRecording()
        {
            m_outputDir.CreateDirectory();
            if (m_quad == null) m_quad = fcAPI.CreateFullscreenQuad();
            if (m_matCopy == null) m_matCopy = new Material(m_shCopy);

            var cam = GetComponent<Camera>();
            if(m_captureTarget == CaptureTarget.FrameBuffer)
            {
                if(m_fbComponents.frameBuffer)
                {
                    if (m_cbCopyFB == null)
                    {
                        int tid = Shader.PropertyToID("_TmpFrameBuffer");
                        m_cbCopyFB = new CommandBuffer();
                        m_cbCopyFB.name = "ImageSequenceRecorder: Copy FrameBuffer";
                        m_cbCopyFB.GetTemporaryRT(tid, -1, -1, 0, FilterMode.Point);
                        m_cbCopyFB.Blit(BuiltinRenderTextureType.CurrentActive, tid);
                        m_cbCopyFB.SetRenderTarget(m_rtFB);
                        m_cbCopyFB.DrawMesh(m_quad, Matrix4x4.identity, m_matCopy, 0, 0);
                        m_cbCopyFB.ReleaseTemporaryRT(tid);
                    }
                    cam.AddCommandBuffer(CameraEvent.AfterEverything, m_cbCopyFB);
                }
                if (m_fbComponents.GBuffer)
                {
                    if (m_cbCopyGB == null)
                    {
                        m_cbCopyGB = new CommandBuffer();
                        m_cbCopyGB.name = "ImageSequenceRecorder: Copy GBuffer";
                        m_cbCopyGB.SetRenderTarget(
                            new RenderTargetIdentifier[] { m_rtGB[0], m_rtGB[1], m_rtGB[2], m_rtGB[3] }, m_rtGB[0]);
                        m_cbCopyGB.DrawMesh(m_quad, Matrix4x4.identity, m_matCopy, 0, 1);
                        m_cbCopyGB.SetRenderTarget(m_rtGB[4]); // depth
                        m_cbCopyGB.DrawMesh(m_quad, Matrix4x4.identity, m_matCopy, 0, 2);
                    }
                    cam.AddCommandBuffer(CameraEvent.BeforeLighting, m_cbCopyGB);
                }
            }
            else if (m_captureTarget == CaptureTarget.RenderTexture)
            {
                if (m_cbCopyRT == null)
                {
                    m_cbCopyRT = new CommandBuffer();
                    m_cbCopyRT.name = "ImageSequenceRecorder: Copy Targets";
                    for (int i = 0; i < m_targetRT.Length; ++i)
                    {
                        m_cbCopyRT.SetRenderTarget(m_rtScratch[i]);
                        m_cbCopyRT.SetGlobalTexture("_TmpRenderTarget", m_targetRT[i]);
                        m_cbCopyRT.DrawMesh(m_quad, Matrix4x4.identity, m_matCopy, 0, 3);
                    }
                    GetComponent<Camera>().AddCommandBuffer(CameraEvent.AfterEverything, m_cbCopyRT);
                }

            }
        }

        public void EndRecording()
        {
            var cam = GetComponent<Camera>();
            if (m_cbCopyFB != null)
            {
                cam.RemoveCommandBuffer(CameraEvent.AfterEverything, m_cbCopyFB);
            }
            if (m_cbCopyGB != null)
            {
                cam.RemoveCommandBuffer(CameraEvent.BeforeLighting, m_cbCopyGB);
            }
            if (m_cbCopyRT != null)
            {
                cam.RemoveCommandBuffer(CameraEvent.AfterEverything, m_cbCopyRT);
            }
        }

        public void OneShot()
        {

        }

        #region impl
        void ReleaseContext()
        {
            if (m_ctx != null)
            {
                m_ctx.Release();
                m_ctx = null;
            }
        }

        bool CreateContext()
        {
            m_ctx = ImageSequenceRecorderContext.Create(m_format);
            return m_ctx != null;
        }

        void ValidateContext()
        {
            if (m_ctx == null)
            {
                CreateContext();
            }
            else
            {
                if (m_ctx.type != m_format)
                {
                    ReleaseContext();
                    CreateContext();
                }
            }
        }

#if UNITY_EDITOR
        void Reset()
        {
            m_shCopy = fcAPI.GetFrameBufferCopyShader();
        }

        void OnValidate()
        {
            m_startFrame = Mathf.Max(1, m_startFrame);
        }
#endif // UNITY_EDITOR

        void OnDisable()
        {
            if (m_cbCopyGB != null)
            {
                m_cbCopyGB.Release();
                m_cbCopyGB = null;
            }
            if (m_cbCopyFB != null)
            {
                m_cbCopyFB.Release();
                m_cbCopyFB = null;
            }
            if (m_cbCopyRT != null)
            {
                m_cbCopyRT.Release();
                m_cbCopyRT = null;
            }

            if (m_rtFB != null)
            {
                m_rtFB.Release();
                m_rtFB = null;
            }
            if (m_rtGB != null)
            {
                foreach (var rt in m_rtGB) { rt.Release(); }
                m_rtGB = null;
            }
            if (m_rtScratch != null)
            {
                foreach (var rt in m_rtScratch) { rt.Release(); }
                m_rtScratch = null;
            }
        }

        void Update()
        {
            int frame = Time.frameCount;

            if (frame == m_startFrame)
            {
                BeginRecording();
            }
            if (frame == m_endFrame + 1)
            {
                EndRecording();
            }
        }

        IEnumerator OnPostRender()
        {
            int frame = Time.frameCount;
            if (frame >= m_startFrame && frame <= m_endFrame)
            {
                yield return new WaitForEndOfFrame();
                Export();
            }
        }
        #endregion
    }

}
