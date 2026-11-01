using UnityEditor;
using UnityEngine;

namespace MindAttic.Chain.EditorTools
{
    /// <summary>
    /// UNI-T20: Audio/Sfx holds the mace's one-shots (<see cref="ChainAudio"/>'s Swing, Impact,
    /// EnemyDeath and GrappleCatch clip sets -- sword_swing_1-7, sword_hit_1-3, death_1-4, defend.wav),
    /// each a few hundred milliseconds of mono PCM; they decompress on load and are preloaded with the
    /// scene, so the first swing or hit never stalls on a main-thread decode the way an
    /// importer left at its own "don't preload" default would. Audio/Music/music.ogg is Sound.vb's
    /// <c>playMusic</c> track (<c>bin\sfx\music\music.wma</c> converted -- Unity cannot import WMA,
    /// BIBLE V11), a 75-second loop that <see cref="MusicPlayer"/> plays start to finish on its own
    /// <c>AudioSource</c>; it streams from disk instead of sitting fully decompressed in memory.
    /// Set here, not by hand in the Inspector, so a reimport never silently drops back to an importer
    /// default that would reintroduce either problem (Unity.Rea.Chain's rebuild standard, point 5).
    /// </summary>
    public sealed class AudioImportSettings : AssetPostprocessor
    {
        private const string SfxFolder = "Assets/Unity.Rea.Chain/Audio/Sfx/";
        private const string MusicFolder = "Assets/Unity.Rea.Chain/Audio/Music/";

        private void OnPreprocessAudio()
        {
            var path = assetPath.Replace('\\', '/');
            var importer = (AudioImporter)assetImporter;
            var settings = importer.defaultSampleSettings;

            if (path.StartsWith(SfxFolder))
            {
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                settings.preloadAudioData = true;
                importer.defaultSampleSettings = settings;
            }
            else if (path.StartsWith(MusicFolder))
            {
                settings.loadType = AudioClipLoadType.Streaming;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = 0.7f;
                importer.defaultSampleSettings = settings;
            }
        }
    }
}
