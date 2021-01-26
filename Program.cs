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
        private const uint OutputTextureSize = 32;

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

            using Pipeline computePipeline = factory.CreateComputePipeline(new ComputePipelineDescription(
                computeShader,
                computeLayout,
                16, 16, 1));

            using DeviceBuffer fillValueBuffer = factory.CreateBuffer(new BufferDescription(FillValueStruct.Size, BufferUsage.UniformBuffer));

            // Create our output texture.
            using Texture computeTargetTexture = factory.CreateTexture(TextureDescription.Texture3D(
                OutputTextureSize,
                OutputTextureSize,
                OutputTextureSize,
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

            // Use the compute shader to fill the texture.
            cl.SetPipeline(computePipeline);
            cl.SetComputeResourceSet(0, computeResourceSet);
            const uint GroupDivisorXY = 16;
            cl.Dispatch(OutputTextureSize / GroupDivisorXY, OutputTextureSize / GroupDivisorXY, OutputTextureSize);

            cl.End();
            graphicsDevice.SubmitCommands(cl);
            graphicsDevice.WaitForIdle();

            // Read back from our texture and make sure it has been properly filled.
            var expectedFillValue = new RgbaFloat(FillValue, FillValue, FillValue, FillValue);
            for (uint depth = 0; depth < computeTargetTexture.Depth; depth++)
            {
                int notFilledCount = CountTexelsNotFilledAtDepth(graphicsDevice, computeTargetTexture, expectedFillValue, depth);

                if (notFilledCount == 0)
                {
                    // Expected behavior:
                    Console.WriteLine($"All texels were properly set at depth {depth}");
                }
                else
                {
                    // Unexpected behavior:
                    uint totalTexels = computeTargetTexture.Width * computeTargetTexture.Height;
                    Console.WriteLine($"{notFilledCount} of {totalTexels} texels were not properly set at depth {depth}");
                }
            }
        }


        /// <summary>
        /// Returns the number of texels in the texture that DO NOT match the fill value.
        /// </summary>
        public static int CountTexelsNotFilledAtDepth<T>(GraphicsDevice device, Texture texture, T fillValue, uint depth)
            where T : struct
        {
            ResourceFactory factory = device.ResourceFactory;

            // We need to create a staging texture and copy into it.
            TextureDescription description = new TextureDescription(texture.Width, texture.Height, depth: 1,
                texture.MipLevels, texture.ArrayLayers,
                texture.Format, TextureUsage.Staging,
                texture.Type, texture.SampleCount);

            Texture staging = factory.CreateTexture(ref description);

            using CommandList cl = factory.CreateCommandList();
            cl.Begin();

            cl.CopyTexture(texture,
                srcX: 0, srcY: 0, srcZ: depth,
                srcMipLevel: 0, srcBaseArrayLayer: 0,
                staging,
                dstX: 0, dstY: 0, dstZ: 0,
                dstMipLevel: 0, dstBaseArrayLayer: 0,
                staging.Width, staging.Height,
                depth: 1, layerCount: 1);

            cl.End();
            device.SubmitCommands(cl);
            device.WaitForIdle();

            try
            {
                MappedResourceView<T> mapped = device.Map<T>(staging, MapMode.Read);

                int notFilledCount = 0;
                for (int y = 0; y < staging.Height; y++)
                {
                    for (int x = 0; x < staging.Width; x++)
                    {
                        T actual = mapped[x, y];
                        if (!fillValue.Equals(actual))
                        {
                            // Don't spam too much.
                            //int spamLimit = 5;
                            //if (notFilledCount < spamLimit)
                            //{
                            //    Console.WriteLine($"({x}, {y}, {depth}) was {actual} instead of {fillValue}");
                            //}

                            //if (notFilledCount == spamLimit)
                            //{
                            //    Console.WriteLine("\t...and even more...");
                            //}

                            notFilledCount++;
                        }
                    }
                }

                return notFilledCount;
            }
            finally
            {
                device.Unmap(staging);
            }
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
