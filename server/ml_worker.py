from models.kmeans_trainer import train as train_kmeans

if __name__ == "__main__":
    print("ML 백그라운드 워커 구동 중...")
    train_kmeans()