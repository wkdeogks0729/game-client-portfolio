using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 보스로부터 ringRadius 떨어진 링 위에
/// count 개의 원형 데미지존을 일정 각도로 배치하는 패턴.
/// 순서:
/// 1) animWindupState 재생
/// 2) windupAnimTime 후 animAttackState 재생 + 링 인디케이터 표시
/// 3) 텔레그래프 windup 끝날 때 VFX → attackDelay 후 DamageZone 생성
/// </summary>
public class Pattern_RingCircles : MonoBehaviour, IBossPattern
{
    [Header("Ring Settings")]
    public int count = 12;           // 링 위에 배치할 개수
    public float ringRadius = 6f;    // 보스로부터 떨어진 거리
    public float circleRadius = 2f;  // 각 데미지 존 반지름
    public float angleOffset = 0f;   // 링 회전(도 단위)

    [Header("Timing")]
    public float windupAnimTime = 0.8f;   // windup 애니 길이(대략)
    public float attackAnimTime = 1.2f;   // 인디케이터 채우는 시간 (Telegraph windup)
    public float attackDelay = 0.3f;      // 인디케이터가 끝난 후 실제 데미지까지 딜레이
    public float keepAfter = 0.15f;       // 폭발 후 인디케이터 유지 시간

    [Header("Pattern End")]
    public float extraEndDelay = 0f;      // 패턴 실질 종료 후, 보스에 "끝났다" 알리기 전 추가 대기

    [Header("Damage")]
    public float damage = 25f;
    public float knockback = 4f;
    public LayerMask hitMask;
    public GameObject damageZoneCirclePrefab; // SphereCollider isTrigger

    [Header("VFX")]
    public GameObject impactVfxPrefab;    // 폭발 시점(혹은 그 직전)에 소환할 이펙트

    // 애니메이션 설정
    [Header("Anim")]
    public string animWindupState = "Idle";         // 시전 시작에 재생할 상태 이름(선택)
    public string animAttackState = "DoubleAttack"; // 인디케이터와 함께 재생할 상태 이름
    public string animAttackTrigger = "";           // 공격 트리거(있으면 우선)

    private GameObject owner;
    private BossController boss;
    private BossAnimDriver anim;
    private bool done;

    public void Begin(GameObject ownerGO)
    {
        owner = ownerGO;
        boss = owner.GetComponent<BossController>();
        anim = owner.GetComponent<BossAnimDriver>();
        done = false;

        // 1) 시전 시작 애니메이션
        if (anim != null && !string.IsNullOrEmpty(animWindupState))
            anim.Play(animWindupState);

        StartCoroutine(Run());
    }

    public bool Tick(float dt) { return !done; }
    public void End() { }

    private IEnumerator Run()
    {
        Vector3 bossPos = owner.transform.position;

        // 링 위의 위치들을 미리 계산
        var centers = new List<Vector3>(count);
        float step = 360f / Mathf.Max(1, count);

        for (int i = 0; i < count; i++)
        {
            float ang = angleOffset + step * i;
            float rad = ang * Mathf.Deg2Rad;

            // XZ 평면에서 원형 배치 (y는 보스와 동일)
            Vector3 dir = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
            Vector3 pos = bossPos + dir * ringRadius;
            centers.Add(pos);
        }

        // 2) windup 애니 끝날 때까지 대기
        if (windupAnimTime > 0f)
        {
            yield return new WaitForSeconds(windupAnimTime);
        }
        // 3) 공격 애니메이션 재생 + 링 인디케이터 띄우기
        if (anim != null)
        {
            boss.PlayAttackSfx(BossAttackSfxType.RingCircles);
            if (!string.IsNullOrEmpty(animAttackTrigger)) anim.Trigger(animAttackTrigger);
            else if (!string.IsNullOrEmpty(animAttackState)) anim.Play(animAttackState);
        }

        if (boss != null)
        {
            foreach (var center in centers)
            {
                Vector3 p = center; // 클로저용 로컬 복사

                // delay 동안 인디케이터가 채워지고,
                // onFire에서 VFX → attackDelay 후 DamageZone 생성
                boss.TelegraphCircle(
                    p,
                    circleRadius,
                    attackAnimTime,
                    () =>
                    {
                        // boss가 이미 죽었으면 아무 것도 안 함
                        if (boss == null || boss.gameObject == null) return;

                        

                        // 보스 쪽에서 코루틴 수행 (패턴 오브젝트가 Destroy되어도 안전)
                        boss.StartCoroutine(
                            SpawnHitSequenceCo(
                                boss,
                                damageZoneCirclePrefab,
                                impactVfxPrefab,
                                p,
                                circleRadius,
                                attackDelay,
                                damage,
                                knockback,
                                hitMask
                            )
                        );
                    },
                    keepAfter
                );
            }
        }
        else
        {
            // BossController 못 찾으면 그냥 즉시 폭발
            foreach (var center in centers)
            {
                SpawnHitImmediate(center);
            }
        }

        // 4) 모든 폭발이 끝난 후 패턴 종료 타이밍 제어
        float baseWait = Mathf.Max(0f, attackAnimTime + keepAfter + attackDelay);
        if (baseWait > 0f)
            yield return new WaitForSeconds(baseWait);

        // 추가로 더 멈춰있고 싶으면 이 값으로 조절
        if (extraEndDelay > 0f)
            yield return new WaitForSeconds(extraEndDelay);

        done = true;
    }

    // ● Static 코루틴: Pattern_RingCircles 인스턴스에 의존하지 않음
    private static IEnumerator SpawnHitSequenceCo(
        BossController boss,
        GameObject dzPrefab,
        GameObject vfxPrefab,
        Vector3 pos,
        float radius,
        float delay,
        float damage,
        float knockback,
        LayerMask hitMask
    )
    {
        // 1) VFX 먼저
        if (vfxPrefab != null)
        {
            Object.Instantiate(
                vfxPrefab,
                pos,
                vfxPrefab.transform.rotation
            );
        }

        // 2) 추가 딜레이
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        // 3) DamageZone 생성
        if (dzPrefab != null)
        {
            var dz = DamageZoneSpawner.SpawnCircle(
                dzPrefab,
                pos,
                radius,
                0.12f,
                damage,
                knockback,
                hitMask
            );

            if (dz != null && boss != null)
                dz.transform.SetParent(boss.DamageZoneRoot, true);
        }
    }

    // 보스/텔레그래프 없이 그냥 즉시 폭발할 때용
    private void SpawnHitImmediate(Vector3 pos)
    {
        if (impactVfxPrefab != null)
        {
            Object.Instantiate(
                impactVfxPrefab,
                pos,
                impactVfxPrefab.transform.rotation
            );
        }

        if (damageZoneCirclePrefab != null)
        {
            var dz = DamageZoneSpawner.SpawnCircle(
                damageZoneCirclePrefab,
                pos,
                circleRadius,
                0.12f,
                damage,
                knockback,
                hitMask
            );

            if (dz != null && boss != null)
                dz.transform.SetParent(boss.DamageZoneRoot, true);
        }
    }
}
