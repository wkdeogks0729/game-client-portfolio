using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

public class NoteGenerator : MonoBehaviour
{
    [Header("노트 데이터")]
    public TextAsset jsonFile;

    [Header("노트 생성")]
    public NotePool notePool;

    [Header("노트 도달 시간 오프셋 (초)")]
    [SerializeField] private float noteArrivalOffset = 3.5f;

    private List<NoteData> notes = new();
    private int currentIndex = 0;
    private double songStartTime;

    private void Start()
    {
        if (notePool == null)
        {
            Debug.LogError("[NoteGenerator] NotePool이 연결되지 않았습니다.");
            return;
        }

        LoadNotesFromJson();
        songStartTime = Time.time;
        StartCoroutine(SpawnNotes());
    }

    private void LoadNotesFromJson()
    {
        if (jsonFile == null)
        {
            Debug.LogError("[NoteGenerator] JSON 파일이 연결되지 않았습니다.");
            return;
        }

        notes = JsonConvert.DeserializeObject<List<NoteData>>(jsonFile.text);
    }

    private IEnumerator SpawnNotes()
    {
        while (currentIndex < notes.Count)
        {
            double elapsedTime = Time.time - songStartTime;

            //  노트 도착 시간에서 offset을 빼고 미리 생성
            if (elapsedTime >= notes[currentIndex].time - noteArrivalOffset)
            {
                SpawnNote(notes[currentIndex]);
                currentIndex++;
            }

            yield return null;
        }
    }

    private void SpawnNote(NoteData data)
    {
        NoteType noteType = ParseNoteType(data.type);
        Note note = notePool.GetNoteComponent(noteType);

        if (note == null)
        {
            Debug.LogWarning($"[NoteManager] {noteType} 노트를 풀에서 가져오지 못했습니다.");
            return;
        }

        Vector2 startPos = GetLaneStartPosition(data.lane);

        note.Initialize(startPos, data.speed, 5.0f, data.lane, data.duration);

        if (noteType == NoteType.Long && note is LongNote longNote)
        {
            longNote.holdDuration = data.duration;
        }
    }

    private NoteType ParseNoteType(string typeStr)
    {
        return typeStr.ToLower() switch
        {
            "long" => NoteType.Long,
            "special" => NoteType.Special,
            _ => NoteType.Normal,
        };
    }

    /// 💡 중심까지 거리 = 10으로 고정됨 (대각선 보정됨)
    private Vector2 GetLaneStartPosition(int lane)
    {
        float r = 10f;
        float diag = r / Mathf.Sqrt(2f); // ≈ 7.071

        return lane switch
        {
            1 => new Vector2(0, r),
            2 => new Vector2(diag, diag),
            3 => new Vector2(r, 0),
            4 => new Vector2(diag, -diag),
            5 => new Vector2(0, -r),
            6 => new Vector2(-diag, -diag),
            7 => new Vector2(-r, 0),
            8 => new Vector2(-diag, diag),
            _ => Vector2.zero,
        };
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.green;

        for (int lane = 1; lane <= 8; lane++)
        {
            Vector2 start = GetLaneStartPosition(lane);
            Vector2 end = Vector2.zero;

            Gizmos.DrawLine(start, end);

            UnityEditor.Handles.color = Color.white;
            UnityEditor.Handles.Label(start + Vector2.up * 0.5f, $"Lane {lane}");
        }
    }
#endif
}
