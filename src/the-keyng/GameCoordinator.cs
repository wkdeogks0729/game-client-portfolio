using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class GameCoordinator : MonoBehaviour
{
    [Serializable]
    public class WorldSlot
    {
        [Header("Identity")]
        public string id = "World";
        public int index;

        [Header("View (optional)")]
        public Camera cam;
        public RenderTexture rt;
        public RawImage view;
        public Graphic extraImage;

        [Header("Respawn/Return")]
        [Tooltip("리셋/ResetPad 시 퍼펫이 돌아올 위치(없으면 씬 초기 위치 사용)")]
        public Transform returnPoint;

        [Header("Actors & Pads")]
        public Puppet puppet;
        public CtrlPad ctrlPad;
        public ArrowPad[] arrowPads;
        public ResetPad[] resetPads;

        [HideInInspector] public bool arrowsGranted;

        [HideInInspector] public CanvasGroup viewCanvasGroup;
        [HideInInspector] public CanvasGroup extraCanvasGroup;
    }

    [SerializeField] private WorldSlot[] worlds;
    [SerializeField] private PyramidWindowLayout windowLayout;
    [SerializeField] private string gameClearSceneName = "GameClear";

    public int CurrentTopWorld { get; private set; }


    private struct HoldState
    {
        public bool up, down, left, right;
        public Vector2 ToVec()
        {
            int x = (right ? 1 : 0) - (left ? 1 : 0);
            int y = (up ? 1 : 0) - (down ? 1 : 0);
            return new Vector2(x, y);
        }
    }
    private HoldState[] holds;

    // 초기 상태 저장
    private Vector3[] initPuppetPos;
    private bool[] initPuppetActive;
    private bool[] initCtrlPadActive;
    private bool[][] initArrowPadsActive;

    private void Awake()
    {
        if (worlds == null) worlds = Array.Empty<WorldSlot>();

        for (int i = 0; i < worlds.Length; i++)
        {
            var w = worlds[i];
            w.index = i;


            if (w.cam != null && w.cam.targetTexture == null) w.cam.enabled = false;
            if (w.view != null)
            {
                w.view.enabled = true;
                w.viewCanvasGroup = GetOrAddCanvasGroup(w.view.rectTransform);
            }
            if (w.extraImage != null)
                w.extraCanvasGroup = GetOrAddCanvasGroup(w.extraImage.rectTransform);


            if (w.ctrlPad != null)
            {
                w.ctrlPad.worldIndex = i;
                w.ctrlPad.OnCtrlTriggered += HandleCtrl;
            }


            if (w.arrowPads != null)
            {
                foreach (var ap in w.arrowPads)
                {
                    if (ap == null) continue;
                    ap.worldIndex = i;
                    ap.OnArrowTriggered += HandleArrowSingle;  
                    ap.OnArrowHoldChanged += HandleArrowHoldChanged;
                }
            }
     
        }


        holds = new HoldState[worlds.Length];

        CacheInitialStates();


        for (int i = 0; i < worlds.Length; i++)
        {
            var w = worlds[i];
            if (w.arrowPads == null) continue;
            foreach (var ap in w.arrowPads)
                if (ap != null) ap.gameObject.SetActive(false);
        }

        if (windowLayout != null)
            windowLayout.SetShownCount(1, false);
    }

    private CanvasGroup GetOrAddCanvasGroup(RectTransform t)
    {
        var cg = t.GetComponent<CanvasGroup>();
        if (cg == null) cg = t.gameObject.AddComponent<CanvasGroup>();
        return cg;
    }

    private void CacheInitialStates()
    {
        int n = worlds.Length;
        initPuppetPos = new Vector3[n];
        initPuppetActive = new bool[n];
        initCtrlPadActive = new bool[n];
        initArrowPadsActive = new bool[n][];

        for (int i = 0; i < n; i++)
        {
            var w = worlds[i];

            if (w.puppet != null)
            {
                initPuppetPos[i] = w.puppet.transform.position;
                initPuppetActive[i] = w.puppet.gameObject.activeSelf;
            }
            else
            {
                initPuppetPos[i] = Vector3.zero;
                initPuppetActive[i] = false;
            }

            if (w.ctrlPad != null) initCtrlPadActive[i] = w.ctrlPad.gameObject.activeSelf;
            else initCtrlPadActive[i] = false;

            if (w.arrowPads != null)
            {
                initArrowPadsActive[i] = new bool[w.arrowPads.Length];
                for (int k = 0; k < w.arrowPads.Length; k++)
                {
                    var ap = w.arrowPads[k];
                    initArrowPadsActive[i][k] = (ap != null && ap.gameObject.activeSelf);
                }
            }
            else initArrowPadsActive[i] = Array.Empty<bool>();
        }


        for (int i = 0; i < n; i++) SetWorldVisible(i, i == 0);
        CurrentTopWorld = 0;
    }

    private void SetWorldVisible(int index, bool visible)
    {
        if (!IsValidWorld(index)) return;
        var w = worlds[index];

        if (w.viewCanvasGroup != null)
            w.viewCanvasGroup.alpha = visible ? 1f : 0f;

        if (w.extraCanvasGroup != null)
            w.extraCanvasGroup.alpha = visible ? 1f : 0f;
    }

    private bool IsValidWorld(int i) => (i >= 0 && i < worlds.Length);


    private void HandleCtrl(int fromWorldIndex)
    {
        if (!IsValidWorld(fromWorldIndex)) return;

        var w = worlds[fromWorldIndex];


        if (w.ctrlPad != null) w.ctrlPad.gameObject.SetActive(false);


        ToggleArrows(w, true);
        w.arrowsGranted = true;

        int next = fromWorldIndex + 1;
        if (!IsValidWorld(next))
        {
            // 더 이상 열 층이 없으면 클리어
            if (!string.IsNullOrWhiteSpace(gameClearSceneName))
                SceneManager.LoadScene(gameClearSceneName);
            return;
        }

      
        SetWorldVisible(next, true);

      
        if (windowLayout != null)
            windowLayout.RevealNext();

        CurrentTopWorld = next;
    }

    private void ToggleArrows(WorldSlot w, bool on)
    {
        if (w.arrowPads == null) return;
        foreach (var ap in w.arrowPads)
            if (ap != null) ap.gameObject.SetActive(on);
    }

    private void HandleArrowSingle(int fromWorldIndex, Direction dir)
    {
        int next = fromWorldIndex + 1;
        if (!IsValidWorld(next)) return;
        var target = worlds[next].puppet;
        if (target == null) return;
        target.StepCommand(dir);
    }

    private void HandleArrowHoldChanged(int fromWorldIndex, Direction dir, bool isHeld)
    {
        if (!IsValidWorld(fromWorldIndex)) return;

        int next = fromWorldIndex + 1;
        if (!IsValidWorld(next)) return;

        var fromW = worlds[fromWorldIndex];
        if (!fromW.arrowsGranted)
        {
            holds[fromWorldIndex] = default;
            var tgt0 = worlds[next].puppet;
            if (tgt0 != null) tgt0.SetRemoteDriveVector(false, Vector2.zero);
            return;
        }

        var tgt = worlds[next].puppet;
        if (tgt == null) return;

        
        ref var h = ref holds[fromWorldIndex];
        switch (dir)
        {
            case Direction.Up: h.up = isHeld; break;
            case Direction.Down: h.down = isHeld; break;
            case Direction.Left: h.left = isHeld; break;
            case Direction.Right: h.right = isHeld; break;
        }

        
        Vector2 vec = h.ToVec();
        bool active = vec.sqrMagnitude > 0f;
        if (active && vec.sqrMagnitude > 1f) vec = vec.normalized;

        tgt.SetRemoteDriveVector(active, active ? vec : Vector2.zero);
    }

    private static Vector2 DirToVec(Direction dir)
    {
        switch (dir)
        {
            case Direction.Up: return Vector2.up;
            case Direction.Down: return Vector2.down;
            case Direction.Left: return Vector2.left;
            case Direction.Right: return Vector2.right;
            default: return Vector2.zero;
        }
    }

    // 완전 리셋 
    public void ResetToStart()
    {

        if (holds != null)
            for (int i = 0; i < holds.Length; i++) holds[i] = default;

        for (int i = 0; i < worlds.Length; i++)
        {
            var w = worlds[i];


            SetWorldVisible(i, i == 0);

            // ArrowPad는 전부 OFF
            if (w.arrowPads != null)
                foreach (var ap in w.arrowPads)
                    if (ap != null) ap.gameObject.SetActive(false);

            // CtrlPad는 ON
            if (w.ctrlPad != null)
                w.ctrlPad.gameObject.SetActive(true);


            if (w.puppet != null)
            {
                Vector3 pos = (w.returnPoint != null ? (Vector3)w.returnPoint.position : initPuppetPos[i]);
                w.puppet.HardResetTo(pos);    
                w.puppet.gameObject.SetActive(initPuppetActive[i]); // 활성은 씬 값 유지
                w.puppet.isDirectControlled = (i == 0);             // 직접조작은 0층만
            }

            w.arrowsGranted = false;
        }

        CurrentTopWorld = 0;

        if (windowLayout != null)
            windowLayout.SetShownCount(1, true);
    }
}
