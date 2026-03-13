from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
import pymysql
import joblib
import os
import pandas as pd
from config import DB_CONFIG, MODEL_PATH
from utils import extract_features

app = FastAPI()

# ML 모델을 서버 구동 시 한 번만 메모리에 로드
MODEL_PATH = 'code_cluster_model.pkl'
code_cluster_model = None
if os.path.exists(MODEL_PATH):
    code_cluster_model = joblib.load(MODEL_PATH)
    print("ML Model Loaded Successfully!")
else:
    print("ML Model not found!")

class CodeSubmitRequest(BaseModel):
    # 유니티에서 받아올 구조 
    user_pk: int
    machine_type: str
    source_code: str
    is_success: bool
    execution_time: float
    output_log: str

def generate_hint(source_code, is_success):
    if not is_success:
        # 실패한 코드일 경우 처리(미정)
        return "실패한 코드"

    features = extract_features(source_code)
    
    if code_cluster_model is not None:
        try:
            user_df = pd.DataFrame([features])
            cluster_id = code_cluster_model.predict(user_df)[0]
            # 방금 분석한 결과에 맞춰서 멘트
            if cluster_id == 0:
                return "너네는 어떠어떠한 친구들이구나 어떠어떠하게 하렴"
            elif cluster_id == 1:
                return "너네는 어떠어떠한 친구들이구나 어떠어떠하게 하렴"
            elif cluster_id == 2:
                return "너네는 어떠어떠한 친구들이구나 어떠어떠하게 하렴"
        except Exception as e:
            print(f"Ai 힌트 생성 중 에러 발생 : {e}")
    return "정상 작동!"
        
@app.post("/api/submit_code")
async def submit_code(request: CodeSubmitRequest):
    conn = pymysql.connect(**DB_CONFIG)
    cursor = conn.cursor()
    try:
        ai_hint = generate_hint(request.source_code, request.is_success)
        calculated_score = 100 - (request.execution_time * 10) - (len(request.source_code) * 0.1)
        if calculated_score < 0: calculated_score = 0

        sql = """
            INSERT INTO code_logs 
            (user_pk, machine_type, source_code, is_success, output_log, execution_time, score, created_at)
            VALUES (%s, %s, %s, %s, %s, %s, %s, NOW())
        """
        cursor.execute(sql, (
            request.user_pk, request.machine_type, request.source_code, 
            request.is_success, request.output_log, request.execution_time, calculated_score
        ))
        conn.commit()
        
        return {
            "status": "success", 
            "score": round(calculated_score, 2),
            "hint": ai_hint 
        }

    except Exception as e:
        print(f"에러 발생: {e}")
        raise HTTPException(status_code=500, detail=str(e))
    finally:
        conn.close()

if __name__ == "__main__":
    import uvicorn
    uvicorn.run("main:app", host="0.0.0.0", port=8000, reload=True)