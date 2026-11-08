using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
public enum BossAttackSfxType
{
    ArmSweep,
    CachedCharge,
    FollowContact,
    RandomCircles,
    RingCircles,
    SummonMinions
}
// 제자리형 보스 컨트롤러
// - HP 퍼센트로 페이즈 판단
// - 현재 페이즈 이하의 모든 패턴을 풀로 묶어 랜덤 선택
// - 패턴 1회 재생 후 쿨다운 대기, 다시 랜덤 선택 반복
// - 평상시에는 항상 플레이어 방향을 향해 부드럽게 회전
// - 패턴 실행 시작~종료 동안에는 회전 정지
public class BossController : MonoBehaviour, IImpactResponder, IImpactVfxProvider
{
    [Header("HP / Stats")]
    public float maxHp = 1000f;
    [NonSerialized] public float currentHp;

    // 선택사항. 없으면 무시
    public MonsterStatData optionalMonsterStatData;

    [Serializable]
    public class Phase
    {
        [Range(0, 100)] public float enterHpPercent = 100f;  // 이 퍼센트 이하가 되면 진입
        public MonoBehaviour[] patternPrefabs;               // IBossPattern 프리팹들
        public float cooldown = 0.4f;                        // 패턴 사이 공통 쿨다운
    }

    [Header("Phases")]
    public Phase[] phases;

    [Header("데미지 미리보기")]
    public GameObject circleIndicator;
    public GameObject rectIndicator;

    [Header("Damage / Spawn Root")]
    [SerializeField] private Transform damageZoneRoot;

    [Header("VFX / Reward")]
    public GameObject hitFx;
    public GameObject deathFx;
    public int reward = 10;

    [Header("부스트 이펙트 위치")]
    public Transform extraFx1Anchor;   // 패턴에서 쓸 추가 이펙트 1 위치
    public Transform extraFx2Anchor;   // 패턴에서 쓸 추가 이펙트 2 위치

    [Header("토네이도 이펙트 위치")]
    public Transform tornadoFxAnchor;   // 패턴에서 쓸 추가 이펙트 1 위치

    [Header("Debug")]
    public bool debugLog = false;

    [Header("회전 설정")]
    public bool autoLook = true;          // 패턴 실행 중이 아닐 때 항상 플레이어 방향으로 회전
    public float lookRotateSpeed = 540f;  // 초당 회전 각도
    public float lookStopAngle = 0.5f;    // 이 각도 이하로 차이나면 정지
    [SerializeField] private float postPatternExtraDelay = 0.5f; // 패턴 종료 직후 추가 대기

    [Header("Death Flow")]
    [Tooltip("죽을 때 애니메이션 bool 파라미터를 켤지 여부")]
    [SerializeField] private bool playDeathAnimation = true;
    [Tooltip("데스 애니메이션용 Animator Bool 파라미터 이름")]
    [SerializeField] private string deathAnimBoolName = "isDead";
    [Tooltip("Animator에서 직접 재생할 Death 스테이트 이름 (예: \"Death\")")]
    [SerializeField] private string deathStateName = "Death";
    [Tooltip("다른 애니에서 Death로 넘어갈 때 크로스페이드 시간")]
    [SerializeField] private float deathStateTransitionTime = 0.05f;
    [Tooltip("애니메이션 재생 후 이 시간만큼 기다렸다가 Fx+파괴")]
    [SerializeField] private float deathDelay = 1.5f;
    [Tooltip("Death 애니메이션이 시작될 때 재생할 VFX")]
    public GameObject deathAnimFx;
    [Tooltip("Death 애니 VFX 기준 위치(비우면 보스 transform)")]
    public Transform deathAnimFxAnchor;

    [Header("Attack SFX")]
    [SerializeField] private AudioClip armSweepSfx;
    [SerializeField] private AudioClip cachedChargeSfx;
    [SerializeField] private AudioClip followContactSfx;
    [SerializeField] private AudioClip randomCirclesSfx;
    [SerializeField] private AudioClip ringCirclesSfx;
    [SerializeField] private AudioClip summonMinionsSfx;
    [Range(0f, 1f)]
    [SerializeField] private float attackSfxVolume = 1f;

    [Header("HpSFX")]
    [SerializeField] private AudioClip deathSfx;
    [Range(0f, 1f)]
    [SerializeField] private float deathSfxVolume = 1f;
    [SerializeField] private AudioClip[] phaseEnterSfx;   // 인덱스 = phaseIndex
    [Range(0f, 1f)]
    [SerializeField] private float phaseSfxVolume = 1f;

    // 내부 상태
    private int phaseIndex = -1;
    private IBossPattern current;
    private GameObject currentGO;
    private bool running;

    // 패턴 실행 중 플래그(실행 시작~끝까지 true)
    private bool _isPatternRunning;
    public bool IsPatternRunning => _isPatternRunning;

    // 전역 일시정지 상태(GameManager.OnStagePause 연동)
    private bool _paused;

    // 이미 죽었는지 여부(중복 Die() 방지)
    private bool _dead;

    public Transform DamageZoneRoot => damageZoneRoot != null ? damageZoneRoot : transform;

    void Awake()
    {
        currentHp = Mathf.Max(1f, maxHp);
    }

    private void OnEnable()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnStagePause += HandleStagePause;
    }

    private void OnDisable()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnStagePause -= HandleStagePause;
    }

    void Start()
    {
        UpdatePhase(true);
    }

    void Update()
    {
        if (_paused) return; // 전역 일시정지 시 보스 로직 정지
        if (_dead) return;   // 죽은 뒤에는 패턴/회전 등 업데이트 중지

        UpdatePhase(false);

        // 패턴 실행 중이 아닐 때는 항상 플레이어 방향으로 부드럽게 회전
        if (autoLook && !_isPatternRunning)
        {
            RotateTowardsPlayerContinuous();
        }

        if (!running || current == null) return;

        bool cont = current.Tick(Time.deltaTime);
        if (!cont) StartCoroutine(NextPattern());
    }

    private void HandleStagePause(bool paused)
    {
        _paused = paused;

        // 애니메이터도 함께 멈춤/재개
        var anim = GetComponentInChildren<Animator>();
        if (anim) anim.speed = paused ? 0f : 1f;
    }

    // 페이즈 판정 및 전환(배열 순서 무관, HP와 가장 가까운 임계값 선택)
    private void UpdatePhase(bool force)
    {
        float hpPercent = (Mathf.Max(0f, currentHp) / Mathf.Max(1f, maxHp)) * 100f;

        int bestIdx = -1;
        float bestThresh = float.MaxValue;

        for (int i = 0; i < phases.Length; i++)
        {
            var ph = phases[i];
            float th = ph != null ? ph.enterHpPercent : float.MaxValue;

            if (hpPercent <= th && th < bestThresh)
            {
                bestThresh = th;
                bestIdx = i;
            }
        }

        if (bestIdx == -1 && phases != null && phases.Length > 0)
        {
            float maxThresh = float.MinValue;
            for (int i = 0; i < phases.Length; i++)
            {
                float th = phases[i].enterHpPercent;
                if (th > maxThresh) { maxThresh = th; bestIdx = i; }
            }
        }

        int next = bestIdx;

        if (force || next != phaseIndex)
        {
            phaseIndex = next;

            // ★ 페이즈 진입 SFX
            PlayPhaseEnterSfx(phaseIndex);

            running = false;
            StopAllCoroutines();
            StopCurrent();
            _isPatternRunning = false; // 안전

            if (phaseIndex >= 0 && phaseIndex < phases.Length)
            {
                if (debugLog)
                {
                    var pool = BuildPatternPool(phaseIndex);
                    string names = string.Join(", ", pool.ConvertAll(p => p ? p.name : "null"));
                    Debug.Log($"[Boss] enter phase={phaseIndex} hp%={hpPercent:F1} pool={pool.Count} [{names}]");
                }

                running = true;
                StartCoroutine(RunPhaseRandomLoop());
            }
        }

    }

    // 랜덤 루프(현재 페이즈 이하 누적 풀에서 선택)
    private IEnumerator RunPhaseRandomLoop()
    {
        while (running)
        {
            var pool = BuildPatternPool(phaseIndex);
            if (pool.Count == 0) { yield return null; continue; }

            var prefab = pool[UnityEngine.Random.Range(0, pool.Count)];
            if (prefab != null)
            {
                currentGO = Instantiate(prefab.gameObject, transform);
                current = currentGO.GetComponent<IBossPattern>();
            }

            if (debugLog)
            {
                string picked = prefab ? prefab.name : "null";
                Debug.Log($"[Boss] Phase {phaseIndex} picked pattern: {picked} (pool={pool.Count})");
            }

            _isPatternRunning = true;
            current?.Begin(gameObject);

            while (running && current != null) yield return null;

            _isPatternRunning = false;

            float cd = Mathf.Max(0f, phases[phaseIndex].cooldown) + Mathf.Max(0f, postPatternExtraDelay);
            for (float t = 0f; running && t < cd; t += Time.deltaTime) yield return null;
        }
    }

    // 현재 페이즈 임계값 기준으로 누적 풀 생성
    private List<MonoBehaviour> BuildPatternPool(int idx)
    {
        var list = new List<MonoBehaviour>();
        if (phases == null || phases.Length == 0) return list;
        if (idx < 0 || idx >= phases.Length) return list;

        var ph = phases[idx];
        if (ph == null || ph.patternPrefabs == null) return list;

        foreach (var mb in ph.patternPrefabs)
        {
            if (mb != null)
                list.Add(mb);
        }

        return list;
    }

    private IEnumerator NextPattern()
    {
        if (debugLog && currentGO)
            Debug.Log($"[Boss] Pattern finished: {currentGO.name}");

        StopCurrent();
        _isPatternRunning = false;
        yield return null;
    }

    private void StopCurrent()
    {
        current?.End();
        current = null;
        if (currentGO) Destroy(currentGO);
        currentGO = null;
    }

    // 데미지/사망
    public void OnImpact(in HitInfo hit)
    {
        if (_dead) return; // 이미 죽은 뒤에는 무시

        if (hitFx) Instantiate(hitFx, hit.point, Quaternion.identity);

        float dmg = Mathf.Max(0f, hit.damage);
        currentHp = Mathf.Max(0f, currentHp - dmg);

        if (DamageTextSpawner.Instance != null)
        {
            Vector3 pos = hit.point.sqrMagnitude > 0.0001f
                ? hit.point
                : transform.position;

            bool isCrit = hit.isCritical;  // 네 HitInfo 구조체에 맞게 사용
            DamageTextSpawner.Instance.Spawn(dmg, pos, isCrit);
        }

        if (currentHp <= 0f)
            Die();
    }

    public GameObject GetImpactFx(in HitInfo hit) => hitFx;

    private void Die()
    {
        if (_dead) return;
        _dead = true;

        running = false;
        StopAllCoroutines();
        StopCurrent();
        _isPatternRunning = false;

        // ★ Death 애니로 갈아타기
        if (playDeathAnimation)
        {
            var anim = GetComponentInChildren<Animator>();
            if (anim)
            {
                if (!string.IsNullOrEmpty(deathAnimBoolName))
                    anim.SetBool(deathAnimBoolName, true);

                if (!string.IsNullOrEmpty(deathStateName))
                {
                    anim.CrossFadeInFixedTime(
                        deathStateName,
                        Mathf.Max(0f, deathStateTransitionTime),
                        0,
                        0f
                    );
                }
            }
        }

        // ★ 여기서 Death SFX 재생
        if (deathSfx)
        {
            var sm = SoundManager.Instance;
            if (sm != null)
            {
                sm.PlaySfx2D(deathSfx, deathSfxVolume);
            }
        }

        // ★ Death 애니메이션과 동시에 재생할 VFX
        if (deathAnimFx)
        {
            Transform anchor = deathAnimFxAnchor ? deathAnimFxAnchor : transform;
            var fx = Instantiate(deathAnimFx, anchor.position, anchor.rotation, anchor);
        }

        // Fx + 실제 파괴는 조금 뒤에
        StartCoroutine(Co_DeathFlow());
    }



    private IEnumerator Co_DeathFlow()
    {
        // 애니메이션 재생 시간만큼 대기
        if (deathDelay > 0f)
            yield return new WaitForSeconds(deathDelay);

        if (deathFx)
            Instantiate(deathFx, transform.position, Quaternion.identity);

        // 보상, 스테이지 연동 지점 (필요하면 여기서 호출)
        // Wallet.Add(reward);
        // FindObjectOfType<GameStageManager>()?.OnMonsterKilled();

        Destroy(gameObject);
    }

    // 텔레그래프 유틸
    public Coroutine TelegraphCircle(Vector3 center, float radius, float windup, Action onFire, float keep = 0.15f)
    {
        return StartCoroutine(TelegraphCircleCo(center, radius, windup, onFire, keep));
    }

    private IEnumerator TelegraphCircleCo(Vector3 center, float radius, float windup, Action onFire, float keep)
    {
        GameObject g = null;
        if (circleIndicator) g = Instantiate(circleIndicator);
        if (g)
        {
            g.transform.position = center;
            g.transform.localScale = new Vector3(radius, 1f, radius);

            var indicator = g.GetComponent<TelegraphIndicator2D>();
            if (indicator != null)
            {
                indicator.Play(windup, keep, onFire);
                yield break; // 나머지는 인디케이터가 알아서 처리
            }
        }

        float t = 0f;
        while (t < windup)
        {
            t += Time.deltaTime;
            yield return null;
        }

        onFire?.Invoke();

        if (g)
        {
            yield return new WaitForSeconds(keep);
            Destroy(g);
        }
    }

    public Coroutine TelegraphRect(Vector3 center, float width, float length, float yawDeg, float windup, Action onFire, float keep = 0.15f)
    {
        return StartCoroutine(TelegraphRectCo(center, width, length, yawDeg, windup, onFire, keep));
    }

    private IEnumerator TelegraphRectCo(Vector3 center, float width, float length, float yawDeg, float windup, Action onFire, float keep)
    {
        GameObject g = null;
        if (rectIndicator) g = Instantiate(rectIndicator);
        if (g)
        {
            g.transform.position = center;
            g.transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);
            g.transform.localScale = new Vector3(width, 1f, length);

            var indicator = g.GetComponent<TelegraphIndicator2D>();
            if (indicator != null)
            {
                indicator.Play(windup, keep, onFire);
                yield break;
            }
        }

        float t = 0f;
        while (t < windup)
        {
            t += Time.deltaTime;
            yield return null;
        }

        onFire?.Invoke();

        if (g)
        {
            yield return new WaitForSeconds(keep);
            Destroy(g);
        }
    }

    // =========================
    // 회전 유틸
    // =========================

    // 그 순간의 플레이어 위치를 기준으로 한 번만 정렬
    public void FacePlayerOnce()
    {
        var player = PlayerController.Instance ? PlayerController.Instance.transform : null;
        if (player == null) return;

        FaceTargetOnce(player.position);
    }

    // 지정 위치를 향해 한 번만 정렬
    public void FaceTargetOnce(Vector3 targetPos)
    {
        Vector3 dir = targetPos - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;

        Quaternion targetRot = Quaternion.LookRotation(dir.normalized, Vector3.up);
        transform.rotation = targetRot;
    }

    // 패턴 실행 중이 아닐 때 매 프레임 부드럽게 플레이어 방향으로 회전
    private void RotateTowardsPlayerContinuous()
    {
        var player = PlayerController.Instance ? PlayerController.Instance.transform : null;
        if (player == null) return;

        Vector3 dir = player.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;

        Quaternion target = Quaternion.LookRotation(dir.normalized, Vector3.up);

        float angle = Quaternion.Angle(transform.rotation, target);
        if (angle <= lookStopAngle) return;

        float step = lookRotateSpeed * Time.deltaTime;
        float t = Mathf.Clamp01(step / Mathf.Max(1e-4f, angle));

        transform.rotation = Quaternion.Slerp(transform.rotation, target, t);
    }

    public void PlayAttackSfx(BossAttackSfxType type)
    {
        AudioClip clip = null;

        switch (type)
        {
            case BossAttackSfxType.ArmSweep:
                clip = armSweepSfx;
                break;
            case BossAttackSfxType.CachedCharge:
                clip = cachedChargeSfx;
                break;
            case BossAttackSfxType.FollowContact:
                clip = followContactSfx;
                break;
            case BossAttackSfxType.RandomCircles:
                clip = randomCirclesSfx;
                break;
            case BossAttackSfxType.RingCircles:
                clip = ringCirclesSfx;
                break;
            case BossAttackSfxType.SummonMinions:
                clip = summonMinionsSfx;
                break;
        }

        if (!clip) return;

        var sm = SoundManager.Instance;
        if (sm == null) return;

        sm.PlaySfx2D(clip, attackSfxVolume);
    }


    // ★ 페이즈 진입 SFX
    private void PlayPhaseEnterSfx(int phaseIdx)
    {
        if (phaseIdx < 0) return;
        if (phaseEnterSfx == null) return;
        if (phaseIdx >= phaseEnterSfx.Length) return;

        var clip = phaseEnterSfx[phaseIdx];
        if (!clip) return;

        var sm = SoundManager.Instance;
        if (sm == null) return;

        sm.PlaySfx2D(clip, phaseSfxVolume);
    }

}
