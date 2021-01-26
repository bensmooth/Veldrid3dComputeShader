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

            if (FillValueStruct.Size != 16)
            {
                throw new Exception($"Expected uniform to be 16 bytes instead of {FillValueStruct.Size}!");
            }

            using DeviceBuffer fillValueBuffer = factory.CreateBuffer(new BufferDescription(FillValueStruct.Size, BufferUsage.UniformBuffer));
            FillValueStruct fillValue = new FillValueStruct(42.42f);

            using CommandList cl = factory.CreateCommandList();
            cl.Begin();
            cl.UpdateBuffer(fillValueBuffer, 0, fillValue);

            // TODO: Actually use our compute shader to fill the texture.
            cl.SetPipeline(computePipeline);

            cl.End();
        }
    }
}
