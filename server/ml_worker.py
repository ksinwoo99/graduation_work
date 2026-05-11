import time
import traceback
from models.kmeans_trainer import train as train_kmeans
from ml.policy_trainer    import train_policy

INTERVAL_SECONDS = 3600

if __name__ == "__main__":
    print(
        f"ML 백그라운드 워커를 시작합니다... ({INTERVAL_SECONDS}초 주기)\n"
        " - KMeans 군집 모델 재학습\n"
        " - Contextual Policy(RandomForest) 재학습 (데이터 충분 시)"
    )

    while True:
        # ── KMeans 재학습 ──────────────────────────────
        try:
            train_kmeans()
        except Exception:
            print(f"[Worker:KMeans] 실행 중 에러:\n{traceback.format_exc()}")

        # ── Contextual Policy 재학습 ───────────────────
        # 데이터 부족(_MIN_TRAINING_SAMPLES 미만) 시 자동 스킵.
        # 학습 성공 시 code_policy_model.pkl 갱신 → main.py 가 mtime 변화로 핫리로드.
        try:
            train_policy()
        except Exception:
            print(f"[Worker:Policy] 실행 중 에러:\n{traceback.format_exc()}")

        print(f"다음 학습까지 {INTERVAL_SECONDS}초 대기 중...\n")
        time.sleep(INTERVAL_SECONDS)
