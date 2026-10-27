using UnityEditor;
using UnityEngine;

namespace BikeGame.EditorTools
{
    /// <summary>
    /// Автоматические настройки импорта картинок из Assets/_Project/Art (ничего не нужно настраивать руками):
    ///   Art/Bike         — спрайты байка и гонщика: 256 пикс/м (шлем head_* — 512, вдвое детальнее), центр, без мипмапов и сжатия;
    ///   Art/Background   — слои параллакса: спрайты 150 пикс/м (небо 100), до 4096 пикселей, сжатие высокого качества;
    ///   Art/Ground       — текстуры земли (и Art/Ground/&lt;локация&gt;): повторяются (тайлятся), с мипмапами;
    ///   Art/Grass        — полосы травы/снега (и Art/Grass/&lt;локация&gt;): повторяются по горизонтали, с мипмапами;
    ///   Art/Grass/Tufts  — кусты у трассы (и Tufts/&lt;локация&gt;): спрайты 680 пикс/м, точка опоры — низ по центру (корни);
    ///   Art/UI           — экранные кнопки: спрайты для интерфейса, без мипмапов, сжатие высокого качества;
    ///   Art/Icon         — иконка приложения: обычная текстура без сжатия (её берёт сборка под Android);
    ///   Art/Wood         — доски и опоры деревянных трамплинов: повторяются в обе стороны;
    ///   Art/FX           — частицы (пыль, пылинки, листья, снежинки, туман, пушинки, перекати-поле): маленькие спрайты.
    /// </summary>
    public class ArtSpriteImporter : AssetPostprocessor
    {
        private const string Art = "Assets/_Project/Art/";
        public const float BackgroundPPU = 150f, SkyPPU = 100f, GrassPPU = 680f;

        /// <summary>Версия правил импорта: при изменении Unity переимпортирует все картинки сам.</summary>
        public override uint GetVersion() => 8;

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Art)) return;
            Apply((TextureImporter)assetImporter);
        }

        /// <summary>Применяет настройки. Возвращает true, если что-то изменилось.</summary>
        public static bool Apply(TextureImporter ti)
        {
            string path = ti.assetPath;
            string before = Snapshot(ti);
            if (path.StartsWith(Art + "Background/")) Background(ti, path.EndsWith("bg_sky.png") ? SkyPPU : BackgroundPPU);
            else if (path.StartsWith(Art + "Ground/")) Tiled(ti, TextureWrapMode.Repeat, path.EndsWith("ground.png") ? TextureWrapMode.Repeat : TextureWrapMode.Clamp);
            else if (path.StartsWith(Art + "Grass/Tufts/")) Sprite(ti, GrassPPU, SpriteAlignment.BottomCenter, true, TextureImporterCompression.CompressedHQ);
            else if (path.StartsWith(Art + "Grass/")) Tiled(ti, TextureWrapMode.Repeat, TextureWrapMode.Clamp);
            else if (path.StartsWith(Art + "UI/Menu/")) { Sprite(ti, 100f, SpriteAlignment.Center, false, TextureImporterCompression.CompressedHQ); ti.maxTextureSize = 2048; }
            else if (path.StartsWith(Art + "UI/")) { Sprite(ti, 100f, SpriteAlignment.Center, false, TextureImporterCompression.CompressedHQ); ti.maxTextureSize = 1024; }
            else if (path.StartsWith(Art + "Icon/")) Icon(ti);
            else if (path.StartsWith(Art + "Wood/")) Tiled(ti, TextureWrapMode.Repeat, TextureWrapMode.Repeat);
            else if (path.StartsWith(Art + "FX/")) { Sprite(ti, 128f, SpriteAlignment.Center, true, TextureImporterCompression.CompressedHQ); ti.maxTextureSize = path.Contains("/sun_") ? 1024 : 256; }
            else if (path.StartsWith(Art + "Bike/head_")) Sprite(ti, FreeRideSceneBuilder.SpritePPU * 2f, SpriteAlignment.Center, false, TextureImporterCompression.Uncompressed);   // шлем — вдвое детальнее
            else Sprite(ti, FreeRideSceneBuilder.SpritePPU, SpriteAlignment.Center, false, TextureImporterCompression.Uncompressed);
            return Snapshot(ti) != before;
        }

        /// <summary>Ключевые настройки одной строкой — чтобы понять, изменилось ли что-то.</summary>
        private static string Snapshot(TextureImporter ti)
        {
            var s = new TextureImporterSettings();
            ti.ReadTextureSettings(s);
            return $"{ti.textureType}|{ti.spriteImportMode}|{ti.spritePixelsPerUnit}|{ti.mipmapEnabled}|{ti.textureCompression}|" +
                   $"{ti.wrapModeU}|{ti.wrapModeV}|{ti.maxTextureSize}|{s.spriteAlignment}|{s.spriteMeshType}|{ti.alphaIsTransparency}|{ti.isReadable}|{ti.npotScale}";
        }

        private static void Sprite(TextureImporter ti, float ppu, SpriteAlignment align, bool mips, TextureImporterCompression comp)
        {
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = ppu;
            ti.mipmapEnabled = mips;
            ti.alphaIsTransparency = true;
            ti.filterMode = FilterMode.Bilinear;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.textureCompression = comp;
            ti.maxTextureSize = 4096;
            var s = new TextureImporterSettings();
            ti.ReadTextureSettings(s);
            s.spriteAlignment = (int)align;
            s.spriteMeshType = SpriteMeshType.FullRect;
            ti.SetTextureSettings(s);
        }

        private static void Icon(TextureImporter ti)
        {
            ti.textureType = TextureImporterType.Default;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.isReadable = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.maxTextureSize = 1024;
        }

        private static void Background(TextureImporter ti, float ppu)
        {
            Sprite(ti, ppu, SpriteAlignment.Center, false, TextureImporterCompression.CompressedHQ);
        }

        private static void Tiled(TextureImporter ti, TextureWrapMode u, TextureWrapMode v)
        {
            ti.textureType = TextureImporterType.Default;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = true;
            ti.filterMode = FilterMode.Trilinear;
            ti.wrapModeU = u;
            ti.wrapModeV = v;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.maxTextureSize = 2048;
            ti.anisoLevel = 2;
        }
    }
}
