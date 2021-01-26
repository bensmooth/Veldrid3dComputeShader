using System;
using System.IO;
using System.Runtime.InteropServices;
using Veldrid;
using Veldrid.SPIRV;


// Intended to demonstrate filling a 3D texture with a compute shader, and then reading it back to ensure it was properly filled.
namespace ComputeShader3d
{
    struct FillValueStruct
    {
        /// <summary>
        /// The value we fill the 3d texture with.
        /// </summary>
        public float FillValue;

        public float pad1, pad2, pad3;

        public FillValueStruct(float fillValue)
        {
            FillValue = fillValue;
            pad1 = pad2 = pad3 = 0;
        }

        public static uint Size => (uint)Marshal.SizeOf<FillValueStruct>();
    }


    class Program
    {
        /// <summary>
        /// The width, height, and depth of the compute texture's output.
        /// </summary>
        private const uint OutputTextureSize = 256;

        /// <summary>
        /// The value we're going to fill the texture with.
        /// </summary>
        private const float FillValue = 42.42f;


        static void Main(GraphicsBackend backend)
        {
            using GraphicsDevice graphicsDevice = CreateDevice(backend);
            Console.WriteLine($"Using backend: {graphicsDevice.BackendType}");
            ResourceFactory factory = graphicsDevice.ResourceFactory;

            byte[] shaderBytes = File.ReadAllBytes("FillComputeShader.comp");
            using Shader computeShader = factory.CreateFromSpirv(new ShaderDescription(
                ShaderStages.Compute,
                shaderBytes,
                "main"));

            using ResourceLayout computeLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("TextureToFill", ResourceKind.TextureReadWrite, ShaderStages.Compute),
                new ResourceLayoutElementDescription("FillValueBuffer", ResourceKind.UniformBuffer, ShaderStages.Compute)));

            ComputePipelineDescription computePipelineDesc = new ComputePipelineDescription(
                computeShader,
                computeLayout,
                16, 16, 1);

            using Pipeline computePipeline = factory.CreateComputePipeline(ref computePipelineDesc);

            using DeviceBuffer fillValueBuffer = factory.CreateBuffer(new BufferDescription(FillValueStruct.Size, BufferUsage.UniformBuffer));

            // Create our output texture.
            using Texture computeTargetTexture = factory.CreateTexture(TextureDescription.Texture2D(
                OutputTextureSize,
                OutputTextureSize,
                1,
                1,
                PixelFormat.R32_G32_B32_A32_Float,
                TextureUsage.Sampled | TextureUsage.Storage));

            using TextureView computeTargetTextureView = factory.CreateTextureView(computeTargetTexture);

            using ResourceSet computeResourceSet = factory.CreateResourceSet(new ResourceSetDescription(
                computeLayout,
                computeTargetTextureView,
                fillValueBuffer));

            using CommandList cl = factory.CreateCommandList();
            cl.Begin();

            cl.UpdateBuffer(fillValueBuffer, 0, new FillValueStruct(FillValue));

            // Actually use our compute shader to fill the texture.
            cl.SetPipeline(computePipeline);
            cl.SetComputeResourceSet(0, computeResourceSet);
            const uint GroupDivisorXY = 16;
            cl.Dispatch(OutputTextureSize / GroupDivisorXY, OutputTextureSize / GroupDivisorXY, groupCountZ: 1);

            cl.End();
            graphicsDevice.SubmitCommands(cl);
            graphicsDevice.WaitForIdle();

            // TODO: Read back from our texture and make sure it has been properly filled.
        }


        private static GraphicsDevice CreateDevice(GraphicsBackend backend)
        {
            GraphicsDeviceOptions options = new GraphicsDeviceOptions(debug: false);

            switch (backend)
            {
                case GraphicsBackend.Direct3D11:
                    return GraphicsDevice.CreateD3D11(options);
                case GraphicsBackend.Vulkan:
                    return GraphicsDevice.CreateVulkan(options);
                case GraphicsBackend.Metal:
                    return GraphicsDevice.CreateMetal(options);
                default:
                    throw new Exception($"Unsupported backend '{backend}'");
            }
        }
    }
}
