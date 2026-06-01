import os
import random
import re
import smtplib
from email.mime.text import MIMEText
from typing import Optional

import mysql.connector
from fastapi import FastAPI
from pydantic import BaseModel
from dotenv import load_dotenv

load_dotenv()

app = FastAPI()

# =========================================================
# [이메일 세팅]
# =========================================================
SMTP_SERVER    = "smtp.gmail.com"
SMTP_PORT      = 587
SENDER_EMAIL   = os.getenv("SMTP_SENDER_EMAIL", "py.factory26@gmail.com")
SENDER_PASSWORD = os.getenv("SMTP_SENDER_PASSWORD", "REDACTED_SMTP_PASSWORD")

# 이메일 형식 검증 정규식 (send_register_auth_code, send_auth_code 공통 사용)
_EMAIL_REGEX = re.compile(r"^[a-zA-Z0-9_.+-]+@[a-zA-Z0-9-]+\.[a-zA-Z0-9-.]+$")

# 인증번호 인메모리 저장소 (재시작 시 초기화됨)
auth_codes_db: dict[str, str] = {}


def send_email(to_email: str, subject: str, content: str) -> None:
    msg = MIMEText(content)
    msg['Subject'] = subject
    msg['From']    = SENDER_EMAIL
    msg['To']      = to_email

    server = smtplib.SMTP(SMTP_SERVER, SMTP_PORT)
    try:
        server.starttls()
        server.login(SENDER_EMAIL, SENDER_PASSWORD)
        server.sendmail(SENDER_EMAIL, to_email, msg.as_string())
    finally:
        server.quit()

# =========================================================
# 1. 데이터 모델 정의
# =========================================================

class UserAuth(BaseModel):
    user_id: Optional[str] = None
    password: Optional[str] = None
    email: Optional[str] = None  
    code: Optional[str] = None

class AuthCodeRequest(BaseModel):
    user_id: str
    email: str

class RegisterCodeRequest(BaseModel):
    email: str

class EmailRequest(BaseModel):
    email: str

class VerifyCodeRequest(BaseModel):
    user_id: str
    email: str
    code: str

# =========================================================
# 2. DB 연결 및 유틸리티
# =========================================================

def get_db_connection():
    return mysql.connector.connect(
        host=os.getenv("DB_HOST", "127.0.0.1"),
        user=os.getenv("DB_USER", "root"),
        password=os.getenv("DB_PASSWORD", "REDACTED_DB_PASSWORD"),
        database=os.getenv("DB_NAME", "game_db"),
    )

@app.get("/")
def read_root(): return {"message": "서버 작동 중"}

# --- 계정 관리 (통합 DB 적용) ---

@app.post("/login")
def login(req: UserAuth):
    conn = get_db_connection(); cursor = conn.cursor()
    try:
        sql = "SELECT pk_id FROM users WHERE id = %s AND password = %s"
        cursor.execute(sql, (req.user_id, req.password))
        user = cursor.fetchone()
        if user: return {"status": "LOGIN_SUCCESS", "msg": "로그인 성공", "user_pk": user[0]}
        return {"status": "LOGIN_FAIL", "msg": "아이디 또는 비밀번호 틀림"}
    finally: conn.close()

@app.post("/check_duplicate")
def check_duplicate(req: UserAuth):
    conn = get_db_connection(); cursor = conn.cursor()
    try:
        cursor.execute("SELECT pk_id FROM users WHERE id = %s", (req.user_id,))
        if cursor.fetchone(): return {"status": "ID_EXIST", "msg": "이미 존재하는 아이디"}
        return {"status": "ID_SAFE", "msg": "사용 가능"}
    finally: conn.close()

# ✨ 이메일로 아이디 찾기 API
@app.post("/find_id_by_email")
def find_id_by_email(req: EmailRequest):
    conn = get_db_connection()
    cursor = conn.cursor()
    try:
        cursor.execute("SELECT id FROM users WHERE email = %s", (req.email,))
        result = cursor.fetchone()
        if result: 
            return {"status": "SUCCESS", "user_id": result[0]}
        else:
            return {"status": "FAIL", "msg": "해당 이메일로 가입된 내역이 없습니다."}
    except Exception as e:
        print(f"[아이디 찾기 DB 에러] {str(e)}") # 서버에서만 확인
        return {"status": "ERROR", "msg": "조회 중 오류가 발생했습니다. 잠시 후 다시 시도해주세요."}
    finally: conn.close()

# ✨ 1. 회원가입용 인증번호 발송 API (통합 DB 저장)
@app.post("/send_register_auth_code")
def send_register_auth_code(req: RegisterCodeRequest):
    if not _EMAIL_REGEX.match(req.email):
        return {"status": "FAIL", "msg": "올바른 이메일 형식이 아닙니다."}

    conn = get_db_connection(); cursor = conn.cursor()
    try:
        cursor.execute("SELECT pk_id FROM users WHERE email = %s", (req.email,))
        if cursor.fetchone():
            return {"status": "FAIL", "msg": "이미 가입된 이메일입니다."}
        
        auth_code = str(random.randint(100000, 999999))
        auth_codes_db[req.email] = auth_code
        
        content = f"안녕하세요, Py.Factory입니다.\n\n회원가입 인증번호는 [{auth_code}] 입니다.\n\n게임 화면에 인증번호를 입력해 주세요."
        send_email(req.email, "[Py.Factory] 회원가입 인증번호", content)
        
        return {"status": "SUCCESS", "msg": f"[{req.email}]로 인증번호가 발송되었습니다."}
    except Exception as e:
        print(f"[회원가입 메일 에러] {str(e)}") # 서버 콘솔 확인용
        return {"status": "ERROR", "msg": "인증 메일 발송에 실패했습니다. (발송 오류)"}
    finally: conn.close()

# ✨ 2. 회원가입 API
@app.post("/register")
def register(req: UserAuth):
    if not req.email or not req.code:
        return {"status": "FAIL", "msg": "이메일 인증을 해주세요."}

    saved_code = auth_codes_db.get(req.email)
    if not saved_code or saved_code != req.code:
        return {"status": "FAIL", "msg": "인증번호가 틀렸거나 만료되었습니다."}

    conn = get_db_connection(); cursor = conn.cursor()
    try:
        sql = "INSERT INTO users (id, password, email) VALUES (%s, %s, %s)"
        cursor.execute(sql, (req.user_id, req.password, req.email))
        conn.commit()
        
        if req.email in auth_codes_db:
            del auth_codes_db[req.email] # 인증 통과 시 파기

        return {"status": "REGISTER_SUCCESS", "msg": "회원가입 완료"}
    except Exception as e:
        print(f"[회원가입 DB 에러] {str(e)}") # 서버에서만 확인
        return {"status": "ERROR", "msg": "회원가입 처리 중 오류가 발생했습니다."}
    finally: conn.close()

# ✨ 3. 비밀번호 찾기용 인증번호 발송 API
@app.post("/send_auth_code")
def send_auth_code(req: AuthCodeRequest):
    if not _EMAIL_REGEX.match(req.email):
        return {"status": "FAIL", "msg": "올바른 이메일 형식이 아닙니다."}

    conn = get_db_connection(); cursor = conn.cursor()
    try:
        cursor.execute("SELECT pk_id FROM users WHERE id = %s AND email = %s", (req.user_id, req.email))
        if not cursor.fetchone():
            return {"status": "FAIL", "msg": "등록된 정보가 없습니다."}
        
        auth_code = str(random.randint(100000, 999999))
        auth_codes_db[req.email] = auth_code

        content = f"안녕하세요, Py.Factory입니다.\n\n{req.user_id}님의 비밀번호 찾기 인증번호는 [{auth_code}] 입니다.\n\n게임 화면에 인증번호를 입력해 주세요."
        send_email(req.email, "[Py.Factory] 비밀번호 찾기 인증번호", content)
        
        return {"status": "SUCCESS", "msg": f"[{req.email}] 이메일로 인증번호가 발송되었습니다."}
    except Exception as e:
        print(f"[비번찾기 메일 에러] {str(e)}") # 서버 콘솔 확인용
        return {"status": "ERROR", "msg": "인증 메일 발송에 실패했습니다. (발송 오류)"}
    finally: conn.close()

# ✨ 4. 비밀번호 찾기 인증번호 확인 및 반환 API
@app.post("/verify_auth_code")
def verify_auth_code(req: VerifyCodeRequest):
    conn = get_db_connection(); cursor = conn.cursor()
    try:
        saved_code = auth_codes_db.get(req.email)
        if not saved_code or saved_code != req.code:
            return {"status": "FAIL", "msg": "인증번호가 틀렸습니다."}
        
        cursor.execute("SELECT password FROM users WHERE id = %s AND email = %s", (req.user_id, req.email))
        result = cursor.fetchone()
        
        del auth_codes_db[req.email] # 확인 후 보안상 파기
        
        if result: 
            return {"status": "SUCCESS", "password": result[0]}
        else:
            return {"status": "FAIL", "msg": "정보 오류"}
    finally: conn.close()

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8002)