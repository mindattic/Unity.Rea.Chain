using System.Collections.Generic;
using MindAttic.Chain.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace MindAttic.Chain
{
    /// <summary>
    /// Root.vb's F1-F3 render states, drawn by URP (UNI-T56). The original sets D3D's <c>FillMode</c> and
    /// <c>CullMode</c> on the device; URP has neither as a switch, so this renderer feature on the game's 2D renderer
    /// (added by RenderPipelineBuilder) redraws the scene the way the screen's <see cref="Options"/> ask, while any of
    /// them is off its start (<see cref="ViewOptions.AltersRendering"/>; otherwise it adds nothing and the game draws as
    /// usual):
    /// <list type="bullet">
    /// <item>it clears the camera's colour to its background and draws every renderer the camera sees again, in the
    /// 2D renderer's own order (sorting layers, then order, then depth), with <see cref="Shader"/>
    /// (<c>Hidden/MindAttic/Chain/ViewOptions</c>), an unlit sprite shader;</item>
    /// <item>F1 (<c>bRenderWireframe</c>) draws it as wireframe, the device's wireframe state
    /// (<see cref="RasterCommandBuffer.SetWireframe"/>, D3D's <c>FillMode.WireFrame</c>);</item>
    /// <item>F2 off (<c>bRenderTextures = False</c>, the menu's "Use Shaded") draws each sprite, line and quad in its
    /// own colour without its texture, as the original's untextured cube is drawn (BIBLE D24);</item>
    /// <item>F3 (<c>bBackFaceCulling</c>) culls back faces: every sprite faces the camera and stays, while a mirrored
    /// one (the angel flipped to face left) turns its back and drops out (BIBLE D25).</item>
    /// </list>
    /// It acts only for game cameras (the Editor's scene view keeps drawing normally), and the HUD, a UI Toolkit
    /// overlay drawn after the camera, stays solid. The redraw is unlit (the 2D lights belong to the 2D renderer's own
    /// passes), so the day's tint and the game-over fade do not show while it is on (BIBLE V14).
    /// </summary>
    public sealed class ViewRenderFeature : ScriptableRendererFeature
    {
        /// <summary>The override shader's name (Assets/Unity.Rea.Chain/Rendering/ViewOptions.shader).</summary>
        public const string ShaderName = "Hidden/MindAttic/Chain/ViewOptions";

        private static readonly int TexturedId = Shader.PropertyToID("_Textured");
        private static readonly int CullId = Shader.PropertyToID("_Cull");

        [Tooltip("The override shader the scene is redrawn with (Hidden/MindAttic/Chain/ViewOptions).")]
        [SerializeField] private Shader shader;

        private Material _material;
        private ViewPass _pass;

        /// <summary>
        /// The screen's view options the feature draws: the enabled <see cref="GameHud"/>'s <see cref="GameHud.Options"/>
        /// (it sets this while enabled). Null draws the scene as usual.
        /// </summary>
        public static ViewOptions Options { get; set; }

        /// <summary>The override shader (set by RenderPipelineBuilder, so it ships in the build).</summary>
        public Shader Shader
        {
            get => shader;
            set
            {
                shader = value;
                DestroyMaterial();
            }
        }

        /// <summary>Whether <paramref name="options"/> asks for this feature's redraw (any of F1-F3 off its start).</summary>
        public static bool Redraws(ViewOptions options) => options != null && options.AltersRendering;

        /// <summary>The cull mode F3 asks for: back faces while <see cref="ViewOptions.BackFaceCulling"/>, none otherwise.</summary>
        public static CullMode CullFor(ViewOptions options) =>
            options != null && options.BackFaceCulling ? CullMode.Back : CullMode.Off;

        /// <summary>The override material, made from <see cref="Shader"/> on first use (null if there is no shader).</summary>
        public Material Material
        {
            get
            {
                if (_material == null)
                {
                    var source = shader != null ? shader : Shader.Find(ShaderName);
                    if (source != null)
                    {
                        _material = CoreUtils.CreateEngineMaterial(source);
                    }
                }

                return _material;
            }
        }

        /// <summary>Sets the override material's texture and cull state from <paramref name="options"/>.</summary>
        public static void Apply(Material material, ViewOptions options)
        {
            material.SetFloat(TexturedId, options == null || options.Textured ? 1f : 0f);
            material.SetFloat(CullId, (float)CullFor(options));
        }

        public override void Create()
        {
            _pass = new ViewPass { renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var options = Options;
            if (!Redraws(options))
            {
                return;
            }

            var material = Material;
            if (material == null)
            {
                return;
            }

            Apply(material, options);
            _pass.Setup(material, options.Wireframe);
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing) => DestroyMaterial();

        private void DestroyMaterial()
        {
            CoreUtils.Destroy(_material);
            _material = null;
        }

        /// <summary>The redraw: clear, then every visible renderer with the override material, wireframe if asked.</summary>
        private sealed class ViewPass : ScriptableRenderPass
        {
            // The 2D renderer's sprite passes (DrawRenderer2DPass), and URP's unlit and forward ones for the chain's line.
            private static readonly List<ShaderTagId> ShaderTags = new List<ShaderTagId>
            {
                new ShaderTagId("SRPDefaultUnlit"),
                new ShaderTagId("Universal2D"),
                new ShaderTagId("UniversalForward"),
                new ShaderTagId("UniversalForwardOnly"),
            };

            private Material _material;
            private bool _wireframe;

            public ViewPass()
            {
                profilingSampler = new ProfilingSampler("Chain View Options");
            }

            public void Setup(Material material, bool wireframe)
            {
                _material = material;
                _wireframe = wireframe;
            }

            private sealed class PassData
            {
                internal RendererListHandle Renderers;
                internal Color Background;
                internal bool Wireframe;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var cameraData = frameData.Get<UniversalCameraData>();
                if (_material == null || cameraData.cameraType != CameraType.Game)
                {
                    return;
                }

                var renderingData = frameData.Get<UniversalRenderingData>();
                var lightData = frameData.Get<UniversalLightData>();
                var resourceData = frameData.Get<UniversalResourceData>();

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Chain View Options", out var data, profilingSampler))
                {
                    var drawing = CreateDrawingSettings(ShaderTags, renderingData, cameraData, lightData, SortingCriteria.CommonTransparent);
                    drawing.overrideMaterial = _material;
                    drawing.overrideMaterialPassIndex = 0;
                    var filtering = new FilteringSettings(RenderQueueRange.all);

                    data.Renderers = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, drawing, filtering));
                    // The camera's own clear colour, already in the active colour space, as the 2D renderer clears with.
                    data.Background = cameraData.backgroundColor;
                    data.Wireframe = _wireframe;

                    builder.UseRendererList(data.Renderers);
                    builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);
                    builder.AllowPassCulling(false);
                    builder.AllowGlobalStateModification(true); // the wireframe state

                    builder.SetRenderFunc(static (PassData pass, RasterGraphContext context) =>
                    {
                        context.cmd.ClearRenderTarget(false, true, pass.Background);
                        if (pass.Wireframe)
                        {
                            context.cmd.SetWireframe(true);
                        }

                        context.cmd.DrawRendererList(pass.Renderers);
                        if (pass.Wireframe)
                        {
                            context.cmd.SetWireframe(false);
                        }
                    });
                }
            }
        }
    }
}
