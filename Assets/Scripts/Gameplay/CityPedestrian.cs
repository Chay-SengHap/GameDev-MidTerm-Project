using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

public class CityPedestrian : MonoBehaviour
{
    private Vector3 start, end, target;
    private AnimationClip idle, walk, greeting, current;
    private Animator animator;
    private PlayableGraph graph;
    private AnimationClipPlayable motion;
    private float waitUntil, nextGreeting, speed;

    internal void Initialize(Vector3 first, Vector3 last, Vector3 lookAt, AnimationClip idleClip,
        AnimationClip walkClip, AnimationClip greetingClip, int index)
    {
        start = first;
        end = target = last;
        idle = idleClip;
        walk = walkClip;
        greeting = greetingClip;
        speed = Random.Range(1.05f, 1.45f);
        waitUntil = Time.time + Random.Range(0f, 2f);
        nextGreeting = Time.time + (index % 2 == 0 ? 2f : 6f);
        Face(first == last ? lookAt : last, 360f);
        animator = GetComponentInChildren<Animator>();
        if (animator == null) return;
        animator.applyRootMotion = false;
        Play(idle);
    }

    private void Update()
    {
        if (start == end)
        {
            if (greeting != null && Time.time >= nextGreeting)
            {
                Play(greeting);
                nextGreeting = Time.time + 8f;
            }
            else if (current == greeting && greeting != null && graph.IsValid() &&
                     motion.GetTime() >= greeting.length)
                Play(idle);
            Loop();
            return;
        }

        if (Time.time < waitUntil) { Loop(); return; }
        Vector3 position = transform.position;
        if ((target - position).sqrMagnitude < 0.04f)
        {
            target = target == end ? start : end;
            waitUntil = Time.time + Random.Range(1f, 3f);
            Play(idle);
            return;
        }

        Vector3 next = Vector3.MoveTowards(position, target, speed * Time.deltaTime);
        if (!CityPedestrians.TryPavement(next, out Vector3 ground))
        {
            target = target == end ? start : end;
            waitUntil = Time.time + 2f;
            Play(idle);
            return;
        }
        Face(target, 300f * Time.deltaTime);
        transform.position = ground;
        Play(walk);
        Loop();
    }

    private void Face(Vector3 point, float degrees)
    {
        Vector3 direction = point - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), degrees);
    }

    private void Play(AnimationClip clip)
    {
        if (animator == null || clip == null || current == clip) return;
        if (graph.IsValid()) graph.Destroy();
        current = clip;
        graph = PlayableGraph.Create("Pedestrian animation");
        motion = AnimationClipPlayable.Create(graph, clip);
        motion.SetApplyFootIK(true);
        var output = AnimationPlayableOutput.Create(graph, "Body", animator);
        output.SetSourcePlayable(motion);
        graph.Play();
    }

    private void Loop()
    {
        if (graph.IsValid() && current != greeting && motion.GetTime() >= current.length && current.length > 0f)
            motion.SetTime(motion.GetTime() % current.length);
    }

    private void OnDestroy()
    {
        if (graph.IsValid()) graph.Destroy();
    }
}
