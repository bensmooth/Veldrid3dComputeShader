using System;
using System.IO;
using System.Runtime.InteropServices;
using Veldrid;
using Veldrid.SPIRV;


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
    }


    class Program
    {
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


        static void Main(GraphicsBackend backend)
        {
            Console.WriteLine($"Using backend: {backend}");

            using GraphicsDevice graphicsDevice = CreateDevice(backend);

            ResourceFactory factory = graphicsDevice.ResourceFactory;

            byte[] shaderBytes = File.ReadAllBytes("FillComputeShader.comp");
            using Shader computeShader = factory.CreateFromSpirv(new ShaderDescription(
                ShaderStages.Compute,
                shaderBytes,
                "main"));

            using var computeLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("Tex", ResourceKind.TextureReadWrite, ShaderStages.Compute),
                new ResourceLayoutElementDescription("ScreenSizeBuffer", ResourceKind.UniformBuffer, ShaderStages.Compute),
                new ResourceLayoutElementDescription("ShiftBuffer", ResourceKind.UniformBuffer, ShaderStages.Compute)));

            ComputePipelineDescription computePipelineDesc = new ComputePipelineDescription(
                computeShader,
                computeLayout,
                16, 16, 1);

            using Pipeline computePipeline = factory.CreateComputePipeline(ref computePipelineDesc);

            using DeviceBuffer fillValueBuffer = factory.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<FillValueStruct>(), BufferUsage.UniformBuffer));
        }
    }
}
