// A picture file on the GPU for ImGui to draw: the way KSA_Splash_Screen does it (TextureAsset -> SimpleVkTexture ->
// an ImGui texture with KSA's linear sampler). Used for the loading pictures and the logo.

using Brutal.ImGuiApi;
using Brutal.Logging;
using Brutal.StbApi;
using Brutal.TextureApi;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;
using KSA;
using RenderCore;

namespace KSAMainMenu;

internal sealed class ImageTexture
{
    public required ImTextureRef Handle;
    public required SimpleVkTexture Texture;
    public required VkImageView View;               // KSA 5707: SimpleVkTexture no longer carries one
    public required int Width;
    public required int Height;

    public float Aspect => (float)Width / Height;

    public static ImageTexture? Load(string path)
    {
        try
        {
            Renderer renderer = Program.GetRenderer();
            var asset = new TextureAsset(path, new Brutal.TextureApi.Stb.Loader.LoadSettings
            {
                ForceChannels = 4,
                ForceDataType = Stb.DataType._8Bit,
            });
            using StagingPool pool = renderer.Allocator.CreateStagingPool(renderer.Graphics, 1);
            var tex = new SimpleVkTexture(renderer.Allocator, pool, asset);
            var info = new VkImageViewCreateInfo
            {
                Image = tex.VkImage,
                ViewType = VkImageViewType._2D,
                Format = tex.Format,
                SubresourceRange = new VkImageSubresourceRange
                {
                    AspectMask = VkImageAspectFlags.ColorBit,
                    LevelCount = tex.MipMapCount,
                    LayerCount = tex.ArrayLayerCount,
                },
            };
            VkImageView view = renderer.Device.CreateImageView(in info, null);
            ImTextureRef handle = ImGuiBackend.Vulkan.AddTexture(Program.LinearClampedSampler, view, VkImageLayout.ShaderReadOnlyOptimal);
            return new ImageTexture { Handle = handle, Texture = tex, View = view, Width = asset.Texture.Width, Height = asset.Texture.Height };
        }
        catch (Exception ex)
        {
            DefaultCategory.Log.Warning($"[KSAMainMenu] picture {path} not loaded: {ex.Message}");
            return null;
        }
    }

    /// Only once no frame in flight can still use it.
    public void Free()
    {
        try
        {
            ImGuiBackend.Vulkan.RemoveTexture(Handle);
            Program.GetRenderer().Device.DestroyImageView(View, null);
            Texture.Dispose();
        }
        catch (Exception ex)
        {
            DefaultCategory.Log.Warning($"[KSAMainMenu] freeing a picture: {ex.Message}");
        }
    }
}
