from fastapi import FastAPI, Query, HTTPException
from pydantic import BaseModel, Field
import mysql.connector
from typing import List, Optional
from datetime import datetime
import ast, asyncio, time, io, traceback
from contextlib import redirect_stdout

import smtplib
from email.mime.text import MIMEText
import random

app = FastAPI()

# =========================================================
# [이메일 세팅]
# =========================================================
SMTP_SERVER = "smtp.gmail.com"
SMTP_PORT = 587
SENDER_EMAIL = "py.factory26@gmail.com" 
SENDER_PASSWORD = "qxeyqsmrpxxbwzza" 

# 🔥 통합 인증번호 메모리장 (열쇠: 이메일 1개로 전부 관리)
auth_codes_db = {} 

def send_email(to_email, subject, content):
    msg = MIMEText(content)
    msg['Subject'] = subject
    msg['From'] = SENDER_EMAIL
    msg['To'] = to_email

    server = smtplib.SMTP(SMTP_SERVER, SMTP_PORT)
    server.starttls()
    server.login(SENDER_EMAIL, SENDER_PASSWORD)
    server.sendmail(SENDER_EMAIL, to_email, msg.as_string())
    server.quit()

# =========================================================
# 1. 데이터 모델 정의
# =========================================================

class UserAuth(BaseModel):
    user_id: Optional[str] = None
    password: Optional[str] = None
    email: Optional[str] = None  
    code: Optional[str] = None  # 🔥 유니티에서 data.code로 보내므로 code로 받음

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

class CodeExecRequest(BaseModel):
    user_id: str
    source_code: str
    machine_type: str = "GENERAL"
    resCommon: int = 0
    resRare: int = 0
    resSpecial: int = 0
    resExotic: int = 0

class CodeLogRequest(BaseModel):
    user_id: str
    source_code: str
    is_success: bool
    output_log: str
    execution_time: float = 0.0

class MachineData(BaseModel):
    machine_type: int
    tile_index: int = 0
    rotation_y: float = 0.0
    source_code: Optional[str] = ""
    pos_x: float = 0.0
    pos_y: float = 0.0
    pos_z: float = 0.0
    
class Config:
    populate_by_name = True                
    allow_population_by_field_name = True  

class GameSaveRequest(BaseModel):
    user_id: str
    res1: int = 0
    res2: int = 0
    res3: int = 0
    res4: int = 0
    res5: int = 0
    play_time: int = 0
    expand_count: int = 0
    quest_id: int = 0
    tutorial_step: int = 0
    conveyor_level: int = 1
    machines: List[MachineData] = []

# =========================================================
# 2. DB 연결 및 유틸리티
# =========================================================

def get_db_connection():
    return mysql.connector.connect(
        host="127.0.0.1",
        user="root",
        password="!kjlgrad26", 
        database="game_db"
    )

def get_user_pk(cursor, user_id_str):
    sql = "SELECT pk_id FROM users WHERE id = %s"
    cursor.execute(sql, (user_id_str,))
    result = cursor.fetchone()
    if result:
        return result['pk_id'] if isinstance(result, dict) else result[0]
    return None

def format_error_user(e, source_code):
    if isinstance(e, SyntaxError):
        line = e.lineno if e.lineno else 1
        msg = e.msg
        offset = e.offset if e.offset else 1
        lines = source_code.split('\n')
        code_line = lines[line-1] if line <= len(lines) else ""
        return (f'  File "<sandbox>", line {line}\n'
                f'    {code_line.strip()}\n'
                f'    {" " * (offset - 1)}^\n'
                f'SyntaxError: {msg}')
    error_type = type(e).__name__
    return f'  File "<sandbox>", line 1\n{error_type}: {str(e)}'

# =========================================================
# 3. 보안 및 AST 엔진
# =========================================================

FORBIDDEN_FUNCTIONS = {"eval", "exec", "open", "__import__", "compile", "globals", "locals"}

class SecurityVisitor(ast.NodeVisitor):
    def visit_Import(self, node): raise Exception("보안: 외부 모듈 사용 금지")
    def visit_ImportFrom(self, node): raise Exception("보안: 외부 모듈 사용 금지")
    def visit_Call(self, node):
        if isinstance(node.func, ast.Name) and node.func.id in FORBIDDEN_FUNCTIONS:
            raise Exception(f"금지 함수 사용: {node.func.id}")
        self.generic_visit(node)

class LoopTransformer(ast.NodeTransformer):
    def visit_While(self, node):
        if isinstance(node.test, ast.Constant) and node.test.value is True:
            has_break = any(isinstance(child, ast.Break) for child in ast.walk(node))
            if not has_break:
                print_node = ast.Expr(
                    value=ast.Call(
                        func=ast.Name(id='print', ctx=ast.Load()),
                        args=[ast.Constant(value="반복합니다.")],
                        keywords=[]
                    )
                )
                node.body.insert(0, print_node)
            node.body.append(ast.Expr(value=ast.Yield(value=None)))
        return node

class Machine:
    def __init__(self, user_id, source_code, resCommon=0, resRare=0, resSpecial=0, resExotic=0):
        self.user_id, self.running = user_id, True
        tree = ast.parse(source_code)
        SecurityVisitor().visit(tree)
        tree = LoopTransformer().visit(tree)
        
        func_def = ast.FunctionDef(
            name="__runner__", 
            args=ast.arguments(posonlyargs=[], args=[], vararg=None, kwonlyargs=[], kw_defaults=[], kwarg=None, defaults=[]),
            body=tree.body, decorator_list=[]
        )
        module_node = ast.Module(body=[func_def], type_ignores=[])
        ast.fix_missing_locations(module_node)
        
        compiled = compile(module_node, "<sandbox>", "exec")
        
        def _mining(target="resCommon", amount=1):
            print(f"[ACTION] MINING_{str(target).upper()}")
            
        def _producting(target="Standard", amount=1):
            print(f"[ACTION] PRODUCTING_{str(target).upper()}")

        def _move(speed="slow"):
            if speed == "fast":
                print("[ACTION] MOVE_FAST")
            else:
                print("[ACTION] MOVE")

        self.env = {
            "__builtins__": {"print":print,"range":range,"len":len,"int":int,"float":float,"str":str,"bool":bool},
            "mining": _mining,
            "producting": _producting,
            "processing": lambda: print("[ACTION] PROCESSING"),
            "machining": lambda: print("[ACTION] MACHINING"),
            "storing": lambda: print("[ACTION] STORING"),
            "selling": lambda: print("[ACTION] SELLING"),
            "move": _move,
            "slow": "slow",
            "fast": "fast",
            
            "resCommon": int(resCommon),
            "resRare": int(resRare),
            "resSpecial": int(resSpecial),
            "resExotic": int(resExotic),
            
            "Standard": "Standard",
            "High": "High",
            "Premium": "Premium",
            "Luxury": "Luxury",
            "Common": "Common", 
            "Rare": "Rare",
            "Special": "Special",
            "Exotic": "Exotic",

            "resCommon_Resource": resCommon,
            "resRare_Resource": resRare,
            "resSpecial_Resource": resSpecial,
            "resExotic_Resource": resExotic,
            "Gold": 0
        }
        exec(compiled, self.env)
        
        result = self.env["__runner__"]()
        if result is not None and hasattr(result, '__next__'):
            self.generator = result
        else:
            self.generator = None
            self.running = False

    def tick(self):
        if not self.running or self.generator is None:
            return ""
        f = io.StringIO()
        with redirect_stdout(f):
            try:
                next(self.generator)
            except StopIteration:
                self.running = False
            except Exception as e:
                print(f"[ERROR] {e}")
                self.running = False
        return f.getvalue().strip()

# =========================================================
# 4. 백그라운드 루프 및 API
# =========================================================

machines = {}

async def game_loop():
    while True:
        start = time.time()
        for m in list(machines.values()):
            m.tick()
        await asyncio.sleep(max(0, 0.5 - (time.time() - start)))

@app.on_event("startup")
async def startup():
    asyncio.create_task(game_loop())

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
        return {"status": "ERROR", "msg": str(e)}
    finally: conn.close()

# ✨ 1. 회원가입용 인증번호 발송 API (통합 DB 저장)
@app.post("/send_register_auth_code")
def send_register_auth_code(req: RegisterCodeRequest):
    conn = get_db_connection(); cursor = conn.cursor()
    try:
        cursor.execute("SELECT pk_id FROM users WHERE email = %s", (req.email,))
        if cursor.fetchone():
            return {"status": "FAIL", "msg": "이미 가입된 이메일입니다."}
        
        auth_code = str(random.randint(100000, 999999))
        auth_codes_db[req.email] = auth_code # 🔥 이메일을 키로 통합 저장소 사용!
        
        content = f"안녕하세요, Py.Factory입니다.\n\n회원가입 인증번호는 [{auth_code}] 입니다.\n\n게임 화면에 인증번호를 입력해 주세요."
        send_email(req.email, "[Py.Factory] 회원가입 인증번호", content)
        
        return {"status": "SUCCESS", "msg": f"[{req.email}]로 인증번호가 발송되었습니다."}
    except Exception as e:
        return {"status": "ERROR", "msg": f"발송 오류: {str(e)}"}
    finally: conn.close()

# ✨ 2. 회원가입 API (통합 DB에서 꺼내서 검사)
@app.post("/register")
def register(req: UserAuth):
    # 유니티에서 data.code 로 보내주므로 req.code 로 검사합니다!
    if not req.email or not req.code:
        return {"status": "FAIL", "msg": "이메일 인증을 해주세요.)"}

    saved_code = auth_codes_db.get(req.email) # 🔥 이메일 키로 꺼내기
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
    except Exception as e: return {"status": "ERROR", "msg": str(e)}
    finally: conn.close()

# ✨ 3. 비밀번호 찾기용 인증번호 발송 API (통합 DB 저장)
@app.post("/send_auth_code")
def send_auth_code(req: AuthCodeRequest):
    conn = get_db_connection(); cursor = conn.cursor()
    try:
        cursor.execute("SELECT pk_id FROM users WHERE id = %s AND email = %s", (req.user_id, req.email))
        if not cursor.fetchone():
            return {"status": "FAIL", "msg": "등록된 정보가 없습니다."}
        
        auth_code = str(random.randint(100000, 999999))
        auth_codes_db[req.email] = auth_code # 🔥 아이디 대신 이메일을 키로 통합 저장!
        
        content = f"안녕하세요, Py.Factory입니다.\n\n{req.user_id}님의 비밀번호 찾기 인증번호는 [{auth_code}] 입니다.\n\n게임 화면에 인증번호를 입력해 주세요."
        send_email(req.email, "[Py.Factory] 비밀번호 찾기 인증번호", content)
        
        return {"status": "SUCCESS", "msg": f"[{req.email}] 이메일로 인증번호가 발송되었습니다."}
    except Exception as e:
        return {"status": "ERROR", "msg": f"발송 오류: {str(e)}"}
    finally: conn.close()

# ✨ 4. 비밀번호 찾기 인증번호 확인 및 반환 API (통합 DB에서 검사)
@app.post("/verify_auth_code")
def verify_auth_code(req: VerifyCodeRequest):
    conn = get_db_connection(); cursor = conn.cursor()
    try:
        saved_code = auth_codes_db.get(req.email) # 🔥 이메일 키로 꺼내기
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

# --- 실행 및 데이터 ---

@app.post("/execute")
def execute_python_code(req: CodeExecRequest):
    conn = get_db_connection(); cursor = conn.cursor()
    start_time = datetime.now()
    user_view_output = ""
    
    try:
        # 1. 유저 확인 (이 부분은 실행 권한 확인을 위해 남겨둡니다)
        user_pk = get_user_pk(cursor, req.user_id)
        if not user_pk: return {"status": "error", "output": "유저 없음"}
        
        # 2. 코드 실행
        f = io.StringIO()
        with redirect_stdout(f):
            new_machine = Machine(req.user_id, req.source_code, req.resCommon, req.resRare, req.resSpecial, req.resExotic)
        
        init_output = f.getvalue().strip()        
        tick_output = new_machine.tick()
        user_view_output = tick_output if tick_output else (init_output if init_output else "실행 완료")
        status = "success"

        # =========================================================
        # ✨ [핵심 추가] 로그 압축 및 멀티라인(줄바꿈) 루프 포맷팅
        # =========================================================
        if user_view_output and status == "success":
            lines = user_view_output.strip().split('\n')
            
            # (선택) 시스템 기본 출력인 "반복합니다." 텍스트를 깔끔하게 숨김
            lines = [line for line in lines if line != "반복합니다."]
            
            compressed_lines = []
            count = 1
            
            for i in range(1, len(lines)):
                # 이전 줄과 현재 줄이 완벽히 똑같다면 카운트 증가
                if lines[i] == lines[i-1]:
                    count += 1
                else:
                    # 다르다면 모아둔 카운트를 출력 (줄바꿈 \n 적용!)
                    if count > 1:
                        compressed_lines.append(f"{count}번 루프 :\n{lines[i-1]}")
                    else:
                        compressed_lines.append(lines[i-1])
                    count = 1
            
            # 마지막 줄 처리
            if lines:
                if count > 1:
                    compressed_lines.append(f"{count}번 루프 :\n{lines[-1]}")
                else:
                    compressed_lines.append(lines[-1])
            
            user_view_output = '\n'.join(compressed_lines)
            
            # 🔥 무한 루프 감지: 파이썬 제너레이터가 살아있다면 (while True 중이라면)
            if new_machine.generator is not None:
                if user_view_output.strip():
                    user_view_output = f"무한 루프 :\n{user_view_output}"
                else:
                    user_view_output = "무한 루프 :\n[상태] 실행 중..."
        # =========================================================
    
    except (SyntaxError, Exception) as e:
        status = "error"
        user_view_output = format_error_user(e, req.source_code)
    
    finally: 
        # DB 연결은 열었으니 안전하게 닫아줍니다!
        conn.close()
    
    # 3. DB 삽입 코드 삭제됨! 순수하게 유니티로 결과만 반환
    execution_time = (datetime.now() - start_time).total_seconds()
    
    return {
        "status": status, 
        "output": user_view_output, 
        "execution_time": execution_time
    }

@app.post("/save/game")
def save_game_data(req: GameSaveRequest):
    conn = get_db_connection()
    cursor = conn.cursor()
    try:
        user_pk = get_user_pk(cursor, req.user_id)
        if not user_pk: return {"status": "ERROR", "msg": "유저 없음"}
        
        sql_res = "INSERT INTO game_saves (user_pk, resource_1, resource_2, resource_3, resource_4, resource_5, total_play_time, expand_count, quest_id, tutorial_step, conveyor_level) VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s) ON DUPLICATE KEY UPDATE resource_1=%s, resource_2=%s, resource_3=%s, resource_4=%s, resource_5=%s, total_play_time=%s, expand_count=%s, quest_id=%s, tutorial_step=%s, conveyor_level=%s"
        val_res = (user_pk, req.res1, req.res2, req.res3, req.res4, req.res5, req.play_time, req.expand_count, req.quest_id, req.tutorial_step, req.conveyor_level, req.res1, req.res2, req.res3, req.res4, req.res5, req.play_time, req.expand_count, req.quest_id, req.tutorial_step, req.conveyor_level)
        cursor.execute(sql_res, val_res)        
        cursor.execute("DELETE FROM installed_machines WHERE user_pk = %s", (user_pk,))
        
        if req.machines:
            sql_mac = "INSERT INTO installed_machines (user_pk, machine_type, tile_index, pos_x, pos_y, pos_z, rotation_y, source_code) VALUES (%s, %s, %s, %s, %s, %s, %s, %s)"
            val_mac = [(user_pk, m.machine_type, m.tile_index, m.pos_x, m.pos_y, m.pos_z, m.rotation_y, m.source_code) for m in req.machines]
            cursor.executemany(sql_mac, val_mac)
            
        conn.commit()
        return {"status": "SUCCESS"}
    except Exception as e:
        conn.rollback()
        return {"status": "ERROR", "msg": str(e)}
    finally: 
        conn.close()

@app.get("/check_save")
def check_save_data(user_id: str):
    conn = get_db_connection()
    cursor = conn.cursor()
    try:
        user_pk = get_user_pk(cursor, user_id)
        if not user_pk: 
            return {"status": "ERROR", "msg": "유저 없음"}

        cursor.execute("SELECT user_pk FROM game_saves WHERE user_pk = %s", (user_pk,))
        
        if cursor.fetchone():
            return {"status": "EXIST"}
        else:
            return {"status": "EMPTY"}
    except Exception as e:
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
        
        cursor.execute("SELECT resource_1, resource_2, resource_3, resource_4, resource_5, total_play_time, expand_count, quest_id, tutorial_step, conveyor_level FROM game_saves WHERE user_pk = %s", (user_pk,))        
        res = cursor.fetchone() or {"resource_1":0, "resource_2":0, "resource_3":0, "resource_4":300, "resource_5":0, "total_play_time":0, "expand_count":0, "quest_id":0, "tutorial_step":0, "conveyor_level":1}
        
        cursor.execute("SELECT machine_type, tile_index, pos_x, pos_y, pos_z, rotation_y, source_code FROM installed_machines WHERE user_pk = %s", (user_pk,))
        mac = cursor.fetchall()
        
        for m in mac:
            m['x'] = m['pos_x']
            m['y'] = m['pos_y']
            m['z'] = m['pos_z']
            
        return {"status": "SUCCESS", "resources": res, "machines": mac}
        
    except Exception as e:
        print(f"[불러오기 에러 발생] {str(e)}")
        return {"status": "ERROR", "msg": f"서버 내부 DB 오류: {str(e)}"}
        
    finally: 
        conn.close()

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8000)