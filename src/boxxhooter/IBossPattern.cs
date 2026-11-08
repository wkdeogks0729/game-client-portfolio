using UnityEngine;

public interface IBossPattern
{
    // 패턴 시작시 1회
    void Begin(GameObject owner);

    // 매 프레임 갱신. true면 계속, false면 종료
    bool Tick(float dt);

    // 패턴 종료시 1회
    void End();
}
