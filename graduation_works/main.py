from fastapi import FastAPI
from pydantic import BaseModel
import mysql.connector
from typing import List

app = FastAPI()

# == 1. 데이터 모델 정의 ==

# 회원가입/로그인용
class UserAuth(BaseModel):
    user_id: str
    password: str = None

# 코드 실행 로그 요청용
class CodeLogRequest(BaseModel):
    user_id: str
    source_code: str
    is_success: bool
    output_log: str
    execution_time: float = 0.0

# 기계 정보
class MachineData(BaseModel):
    machine_type: int
    x: float
    y: float
    z: float

# 게임 저장 (자원 + 기계)
class GameSaveRequest(BaseModel):
    user_id: str
    res1: int
    res2: int
    res3: int
    res4: int
    play_time: int
    machines: List[MachineData] = []


# == 2. DB 연결 함수 ==
def get_db_connection():
    return mysql.connector.connect(
        host="127.0.0.1",
        user="root",
        password="REDACTED_DB_PASSWORD",
        database="game_db"
    )

def get_user_pk(cursor, user_id_str):
    sql = "SELECT pk_id FROM users WHERE id = %s"
    cursor.execute(sql, (user_id_str,))
    result = cursor.fetchone()
    if result: return result[0]
    return None

# == 3. 로그인, 회원가입 등

@app.get("/")
def read_root():
    return {"message": "서버 작동"}

# [로그인]
@app.post("/login")
def login(req: UserAuth):
    conn = get_db_connection()
    cursor = conn.cursor()
    try:
        sql = "SELECT pk_id FROM users WHERE id = %s AND password = %s"
        cursor.execute(sql, (req.user_id, req.password))
        user = cursor.fetchone()
        
        if user:
            # Node.js: "LOGIN_SUCCESS"
            return {"status": "LOGIN_SUCCESS", "msg": "로그인 성공", "user_pk": user[0]}
        else:
            # Node.js: "LOGIN_FAIL"
            return {"status": "LOGIN_FAIL", "msg": "실패"}
    finally:
        conn.close()

# [아이디 중복 확인]
@app.post("/check_duplicate")
def check_duplicate(req: UserAuth):
    conn = get_db_connection()
    cursor = conn.cursor()
    try:
        cursor.execute("SELECT pk_id FROM users WHERE id = %s", (req.user_id,))
        if cursor.fetchone():
            return {"status": "ID_EXIST", "msg": "이미 존재하는 아이디"}
        else:
            return {"status": "ID_SAFE", "msg": "사용 가능"}
    finally:
        conn.close()

# [회원가입]
@app.post("/register")
def register(req: UserAuth):
    conn = get_db_connection()
    cursor = conn.cursor()
    try:
        sql = "INSERT INTO users (id, password) VALUES (%s, %s)"
        cursor.execute(sql, (req.user_id, req.password))
        conn.commit()
        return {"status": "REGISTER_SUCCESS", "msg": "회원가입 완료"}
    except Exception as e:
        return {"status": "ERROR", "msg": str(e)}
    finally:
        conn.close()

# [비밀번호 찾기]
@app.post("/find_pw")
def find_pw(req: UserAuth):
    conn = get_db_connection()
    cursor = conn.cursor()
    try:
        cursor.execute("SELECT password FROM users WHERE id = %s", (req.user_id,))
        result = cursor.fetchone()
        if result:
            return {"status": "SUCCESS", "password": result[0]}
        else:
            return {"status": "USER_NOT_FOUND", "msg": "유저 없음"}
    finally:
        conn.close()


# == 4. 게임 데이터 기능 (로그, 저장, 불러오기) ==

@app.post("/log/code")
def log_code_execution(req: CodeLogRequest):
    conn = get_db_connection()
    cursor = conn.cursor()
    try:
        user_pk = get_user_pk(cursor, req.user_id)
        if not user_pk: return {"status": "ERROR", "msg": "유저 없음"}

        sql = "INSERT INTO code_logs (user_pk, source_code, is_success, output_log, execution_time) VALUES (%s, %s, %s, %s, %s)"
        cursor.execute(sql, (user_pk, req.source_code, req.is_success, req.output_log, req.execution_time))
        conn.commit()
        return {"status": "SUCCESS"}
    finally:
        conn.close()

@app.post("/save/game")
def save_game_data(req: GameSaveRequest):
    conn = get_db_connection()
    cursor = conn.cursor()
    try:
        user_pk = get_user_pk(cursor, req.user_id)
        if not user_pk: return {"status": "ERROR", "msg": "유저 없음"}

        # 1. 자원 저장
        sql_res = """
            INSERT INTO game_saves (user_pk, resource_1, resource_2, resource_3, resource_4, total_play_time)
            VALUES (%s, %s, %s, %s, %s, %s)
            ON DUPLICATE KEY UPDATE
            resource_1=%s, resource_2=%s, resource_3=%s, resource_4=%s, total_play_time=%s
        """
        val_res = (user_pk, req.res1, req.res2, req.res3, req.res4, req.play_time,
                   req.res1, req.res2, req.res3, req.res4, req.play_time)
        cursor.execute(sql_res, val_res)

        # 2. 기계 저장
        cursor.execute("DELETE FROM installed_machines WHERE user_pk = %s", (user_pk,))
        if req.machines:
            sql_mac = "INSERT INTO installed_machines (user_pk, machine_type, pos_x, pos_y, pos_z) VALUES (%s, %s, %s, %s, %s)"
            val_mac = [(user_pk, m.machine_type, m.x, m.y, m.z) for m in req.machines]
            cursor.executemany(sql_mac, val_mac)

        conn.commit()
        return {"status": "SUCCESS"}
    except Exception as e:
        conn.rollback()
        return {"status": "ERROR", "msg": str(e)}
    finally:
        conn.close()

@app.get("/load/game")
def load_game_data(user_id: str):
    conn = get_db_connection()
    cursor = conn.cursor(dictionary=True)
    try:
        user_pk = get_user_pk(cursor, user_id)
        if not user_pk: return {"status": "ERROR", "msg": "유저 없음"}

        cursor.execute("SELECT resource_1, resource_2, resource_3, resource_4, total_play_time FROM game_saves WHERE user_pk = %s", (user_pk,))
        res = cursor.fetchone() or {"resource_1":0, "resource_2":0, "resource_3":0, "resource_4":0, "total_play_time":0}

        cursor.execute("SELECT machine_type, pos_x, pos_y, pos_z FROM installed_machines WHERE user_pk = %s", (user_pk,))
        mac = cursor.fetchall()

        return {"status": "SUCCESS", "resources": res, "machines": mac}
    finally:
        conn.close()