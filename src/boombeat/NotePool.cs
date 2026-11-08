using System.Collections.Generic;
using UnityEngine;

public sealed class NotePool : MonoBehaviour
{
    [Header("노트 프리팹")]
    [SerializeField] private Note normalNotePrefab;
    [SerializeField] private Note longNotePrefab;

    [Header("풀 초기 크기")]
    [SerializeField, Min(1)] private int initialPoolSize = 30;

    // 현재 "꺼내 쓸 수 있는" 비활성 노트들
    private readonly Dictionary<NoteType, Queue<Note>> availablePools = new();

    // 타입별 원본 프리팹
    private readonly Dictionary<NoteType, Note> prefabMap = new();

    // 이미 풀에 들어와 있는 노트 추적 (중복 반환 방지)
    private readonly HashSet<Note> inactiveNotes = new();

    private void Awake()
    {
        BuildPrefabMap();
        Prewarm(NoteType.Normal);
        Prewarm(NoteType.Long);
    }

    private void BuildPrefabMap()
    {
        prefabMap.Clear();

        if (normalNotePrefab != null)
            prefabMap[NoteType.Normal] = normalNotePrefab;
        else
            Debug.LogError("[NotePool] normalNotePrefab이 비어 있습니다.");

        if (longNotePrefab != null)
            prefabMap[NoteType.Long] = longNotePrefab;
        else
            Debug.LogError("[NotePool] longNotePrefab이 비어 있습니다.");
    }

    private void Prewarm(NoteType type)
    {
        Queue<Note> pool = GetOrCreatePool(type);

        for (int i = 0; i < initialPoolSize; i++)
        {
            Note note = CreateNewNote(type);
            if (note == null) continue;

            pool.Enqueue(note);
            inactiveNotes.Add(note);
        }
    }

    private Queue<Note> GetOrCreatePool(NoteType type)
    {
        if (!availablePools.TryGetValue(type, out Queue<Note> pool))
        {
            pool = new Queue<Note>();
            availablePools[type] = pool;
        }

        return pool;
    }

    private Note CreateNewNote(NoteType type)
    {
        if (!prefabMap.TryGetValue(type, out Note prefab) || prefab == null)
        {
            Debug.LogWarning($"[NotePool] {type} 타입 프리팹이 등록되지 않았습니다.");
            return null;
        }

        Note note = Instantiate(prefab, transform);
        note.BindPool(this, type);
        note.gameObject.SetActive(false);

        return note;
    }

    /// <summary>
    /// 풀에서 노트를 꺼낸다. 남은 노트가 없으면 null 반환.
    /// </summary>
    public Note GetNote(NoteType type)
    {
        Queue<Note> pool = GetOrCreatePool(type);

        while (pool.Count > 0)
        {
            Note note = pool.Dequeue();

            // 혹시 외부에서 파괴된 오브젝트면 건너뜀
            if (note == null)
                continue;

            inactiveNotes.Remove(note);

            note.ResetState();
            note.gameObject.SetActive(true);

            return note;
        }

        Debug.LogWarning($"[NotePool] {type} 풀에 남은 노트가 없습니다.");
        return null;
    }

    /// <summary>
    /// 노트를 풀에 반환한다.
    /// </summary>
    public void ReturnNote(Note note)
    {
        if (note == null)
            return;

        if (inactiveNotes.Contains(note))
        {
            Debug.LogWarning($"[NotePool] 이미 반환된 노트입니다: {note.name}");
            return;
        }

        NoteType type = note.NoteType;
        Queue<Note> pool = GetOrCreatePool(type);

        note.ResetState();
        note.transform.SetParent(transform, false);
        note.gameObject.SetActive(false);

        inactiveNotes.Add(note);
        pool.Enqueue(note);
    }
    public Note GetNoteComponent(NoteType type)
    {
        return GetNoteInternal(type);
    }

    private Note GetNoteInternal(NoteType type)
    {
        Queue<Note> pool = GetOrCreatePool(type);

        while (pool.Count > 0)
        {
            Note note = pool.Dequeue();

            if (note == null)
                continue;

            inactiveNotes.Remove(note);

            note.ResetState();
            note.gameObject.SetActive(true);

            return note;
        }

        Debug.LogWarning($"[NotePool] {type} 풀에 남은 노트가 없습니다.");
        return null;
    }
}
