using UnityEditor;
using UnityEngine;

namespace BikeGame.EditorTools
{
    /// <summary>
    /// Импорт звуков из Assets/_Project/Audio (настраивать руками не нужно):
    /// петли (*_loop) — без сжатия (PCM), чтобы стык петли был бесшовным; короткие удары — ADPCM.
    /// Всё распаковывается при загрузке (Decompress On Load) — без задержки при первом звуке на телефоне.
    /// </summary>
    public class AudioImportRules : AssetPostprocessor
    {
        private const string Folder = "Assets/_Project/Audio/";
        public override uint GetVersion() => 1;

        private void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(Folder)) return;
            var ai = (AudioImporter)assetImporter;
            ai.forceToMono = true;
            ai.loadInBackground = false;
            AudioImporterSampleSettings s = ai.defaultSampleSettings;
            s.loadType = AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = assetPath.Contains("_loop") ? AudioCompressionFormat.PCM : AudioCompressionFormat.ADPCM;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            ai.defaultSampleSettings = s;
        }
    }
}
