using UnityEngine;

public class GameplaySoundtrack : MonoBehaviour
{
    [SerializeField] private AudioClip[] tracks;
    [SerializeField, Range(0f, 1f)] private float volume = 0.25f;

    private AudioSource source;
    private int nextTrack;

    private void Awake()
    {
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.volume = volume;
    }

    private void Update()
    {
        if (AudioListener.pause || source.isPlaying || tracks == null || tracks.Length == 0)
            return;

        for (int i = 0; i < tracks.Length; i++)
        {
            AudioClip track = tracks[nextTrack];
            nextTrack = (nextTrack + 1) % tracks.Length;
            if (track == null) continue;
            source.clip = track;
            source.Play();
            return;
        }
    }
}
