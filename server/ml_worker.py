import time
import traceback
from models.kmeans_trainer import train as train_kmeans

INTERVAL_SECONDS = 3600

if __name__ == "__main__":
    print(f"ML 백그라운드 워커를 시작합니다... ({INTERVAL_SECONDS}초 주기)")

    while True:
        try:
            train_kmeans()
        except Exception:
            print(f"워커 실행 중 에러 발생:\n{traceback.format_exc()}")

        print(f"다음 학습까지 {INTERVAL_SECONDS}초 대기 중...\n")
        time.sleep(INTERVAL_SECONDS)
