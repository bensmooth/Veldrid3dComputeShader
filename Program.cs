using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.SPIRV;
using Veldrid.StartupUtilities;


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
        private const uint OutputTextureSize = 16;

        /// <summary>
        /// The value we're going to fill the texture with, multiplied by the current depth.
        /// </summary>
        private const float FillValue = 1f / OutputTextureSize;


        /// <summary>
        /// Runs the test.
        /// </summary>
        /// <param name="backend">The rendering backend to use.</param>
        /// <param name="capture">Set to true to capture with RenderDoc.</param>
        static void Main(GraphicsBackend backend, bool capture)
        {
            RenderDoc renderDoc = null;

            if (capture)
            {
                RenderDoc.Load(out renderDoc);

                renderDoc.APIValidation = true;
                renderDoc.CaptureAllCmdLists = true;
                renderDoc.RefAllResources = true;
                renderDoc.VerifyBufferAccess = true;

                renderDoc.SetCaptureSavePath(backend.ToString());
            }

            using GraphicsDevice graphicsDevice = CreateDevice(backend);
            Console.WriteLine($"Using backend: {graphicsDevice.BackendType}");

            renderDoc?.StartFrameCapture();

            Test(graphicsDevice);
            renderDoc?.EndFrameCapture();

            renderDoc?.LaunchReplayUI(Path.Combine(Directory.GetCurrentDirectory(), $"{backend}_capture.rdc"));
        }


        private static void Test(GraphicsDevice graphicsDevice)
        {
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
            for (uint depth = 0; depth < computeTargetTexture.Depth; depth++)
            {
                RgbaFloat expectedFillValue = new RgbaFloat(new Vector4(FillValue * (depth + 1)));
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
        public unsafe static int CountTexelsNotFilledAtDepth<T>(GraphicsDevice device, Texture texture, T fillValue, uint depth)
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

                //int sizeOfT = Marshal.SizeOf<T>();
                //Span<byte> byteSpan = new Span<byte>(mapped.MappedResource.Data.ToPointer(), (int)mapped.MappedResource.SizeInBytes);
                //int rowLengthBytes = (int)(sizeOfT * staging.Width);

                int notFilledCount = 0;
                for (int y = 0; y < staging.Height; y++)
                {
                    //int rowOffset = (int)(mapped.MappedResource.RowPitch * y);
                    //Span<byte> currentRowBytes = byteSpan.Slice(rowOffset, rowLengthBytes);
                    //Span<T> currentRowSpan = MemoryMarshal.Cast<byte, T>(currentRowBytes);

                    for (int x = 0; x < staging.Width; x++)
                    {
                        T actual = mapped[x, y];
                        //T actualFromSpan = currentRowSpan[x];

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
                case GraphicsBackend.OpenGL:
                    WindowCreateInfo windowCI = new WindowCreateInfo()
                    {
                        X = 100,
                        Y = 100,
                        WindowWidth = 100,
                        WindowHeight = 100,
                        WindowTitle = "OGL test"
                    };

                    Sdl2Window window = VeldridStartup.CreateWindow(ref windowCI);
                    return VeldridStartup.CreateDefaultOpenGLGraphicsDevice(options, window, GraphicsBackend.OpenGL);
                default:
                    throw new Exception($"Unsupported backend '{backend}'");
            }
        }
    }
}
