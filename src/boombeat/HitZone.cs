using UnityEngine;
using System.Collections.Generic;
using System.Collections;

public class HitZone : MonoBehaviour
{
    public int lane;
    public float perfectRange = 0.3f;
    public float goodRange = 0.6f;

    private List<Note> overlappingNotes = new();
    private Vector3 originalScale;
    private float scaleAmount = 1.2f;
    private float scaleDuration = 0.1f;

    private void Awake()
    {
        originalScale = transform.localScale;                  // 스케일 저장
    }

    /// <summary>
    /// 노트 타격 하는지 검사
    /// </summary>
    private void Update()
    {
        if (InputManager.IsDirectionKeyPressed(lane))
        {
            TryHitNote();
        }
    }

    /// <summary>
    /// Note 콜라이더 충돌(들어올때)시 overlappingNotes에 추가
    /// </summary>
    /// <param name="other"></param>
    private void OnTriggerEnter2D(Collider2D other)
    {
        Note note = GetNoteFromCollider(other);
        if (note != null && !overlappingNotes.Contains(note))
        {
            overlappingNotes.Add(note);
        }
    }

    /// <summary>
    /// 타격 실패시 overlappingNotes에서 제거 후 miss처리
    /// </summary>
    /// <param name="other"></param>
    private void OnTriggerExit2D(Collider2D other)
    {
        Note note = GetNoteFromCollider(other);
        if (note != null)
        {
            overlappingNotes.Remove(note);

            // 노트가 롱노트인 경우, 헤드가 지나갔을 때 미스 처리
            if (note is LongNote longNote && longNote.State == NoteState.None)
            {
                longNote.ForceMiss();
            }
            else if (note.State == NoteState.None)
            {
                note.ForceMiss();
            }
        }
    }

    /// <summary>
    /// 노트 타격시 perfect와 good 측정
    /// </summary>
    private void TryHitNote()
    {
        for (int i = 0; i < overlappingNotes.Count; i++)
        {
            Note note = overlappingNotes[i];
            if (note == null || note.State != NoteState.None)                               // 노트 타격전 검사
                continue;

            //hitzone과 note의 거리 측정
            float distance = Vector2.Distance(transform.position, note.transform.position);
            StartCoroutine(PulseScale());                                                   // 타격시 시각적 효과

            // 롱노트가 HitZone에 들어왔을 때, 헤드와의 거리 비교 후 타격 처리
            if (note is LongNote longNote)
            {
                // 롱노트 헤드의 위치와 HitZone의 거리 계산
                float headDistance = Vector2.Distance(transform.position, longNote.headNote.position);

                if (headDistance <= perfectRange)
                {
                    longNote.TryStartHold(this);
                    NoteManager.Instance.OnNoteHit(longNote, "Perfect");
                    return;
                }
                else if (headDistance <= goodRange)
                {
                    longNote.TryStartHold(this);
                    NoteManager.Instance.OnNoteHit(longNote, "Good");
                    return;
                }
            }
            else
            {
                // 일반 노트 타격 처리
                if (distance <= perfectRange)
                {
                    note.MarkAsHit();
                    NoteManager.Instance.OnNoteHit(note, "Perfect");
                    return;
                }
                else if (distance <= goodRange)
                {
                    note.MarkAsHit();
                    NoteManager.Instance.OnNoteHit(note, "Good");
                    return;
                }
            }
        }
    }

    //노트 collider 저장
    private Note GetNoteFromCollider(Collider2D col)
    {
        return col.GetComponent<Note>() ?? col.GetComponentInParent<Note>();
    }

    // 노트 조금 커지게 하는
    private IEnumerator PulseScale()
    {
        transform.localScale = originalScale * scaleAmount;

        float elapsed = 0f;
        while (elapsed < scaleDuration)
        {
            transform.localScale = Vector3.Lerp(
                originalScale * scaleAmount,
                originalScale,
                elapsed / scaleDuration
            );
            elapsed += Time.deltaTime;
            yield return null;
        }

        transform.localScale = originalScale;
    }
}
