import os
import ast
import asyncio
import time
import io
import itertools
from contextlib import redirect_stdout
from datetime import datetime
from typing import List, Optional

import mysql.connector
from fastapi import FastAPI
from pydantic import BaseModel
from dotenv import load_dotenv

load_dotenv()

app = FastAPI()

# =========================================================
# 1. 데이터 모델 정의
# =========================================================

class CodeExecRequest(BaseModel):
    user_id: str
    source_code: str
    machine_type: str = "GENERAL"
    resCommon: int = 0
    resRare: int = 0
    resSpecial: int = 0
    resExotic: int = 0

class MachineData(BaseModel):
    machine_type: int
    tile_index: int = 0
    rotation_y: float = 0.0
    source_code: Optional[str] = ""
    pos_x: float = 0.0
    pos_y: float = 0.0
    pos_z: float = 0.0

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
    conveyor_level: int = 0
    machines: List[MachineData] = []

class RecommendRequest(BaseModel):
    from_user_id: str
    to_user_id: str

# =========================================================
# 2. DB 연결 및 유틸리티
# =========================================================

def get_db_connection():
    return mysql.connector.connect(
        host=os.getenv("DB_HOST", "127.0.0.1"),
        user=os.getenv("DB_USER", "root"),
        password=os.getenv("DB_PASSWORD", ""),
        database=os.getenv("DB_NAME", "game_db"),
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

# 화이트리스트: { module_name: {허용 심볼들} }
# 다른 모든 import 는 SecurityVisitor 에서 차단됩니다.
ALLOWED_IMPORT_FROMS: dict[str, set[str]] = {
    "itertools": {"count"},
}


def _safe_import(name, globals=None, locals=None, fromlist=(), level=0):
    """
    Machine sandbox 의 __builtins__['__import__'] 로 주입되는 제한 import.
    화이트리스트(ALLOWED_IMPORT_FROMS)에 등재된 모듈만 통과시킵니다.

    유저가 `__import__('os')` 같이 직접 호출하는 경로는 SecurityVisitor 의
    FORBIDDEN_FUNCTIONS 체크에서 이미 차단되므로, 본 함수는 `from X import Y`
    같은 import 문이 내부적으로 호출하는 경로만 처리합니다.
    """
    if level == 0 and name in ALLOWED_IMPORT_FROMS:
        if name == "itertools":
            return itertools
    raise Exception(f"보안: 외부 모듈 사용 금지 ({name})")


class SecurityVisitor(ast.NodeVisitor):
    def visit_Import(self, node):
        # `import X` 형태는 전부 차단 — 화이트리스트는 `from X import Y` 만 허용
        raise Exception("보안: 외부 모듈 사용 금지")

    def visit_ImportFrom(self, node):
        # 화이트리스트된 모듈 + 심볼 조합만 통과
        allowed = ALLOWED_IMPORT_FROMS.get(node.module, set())
        bad = [a.name for a in node.names if a.name not in allowed]
        if bad:
            raise Exception(
                f"보안: 외부 모듈 사용 금지 ({node.module}.{bad[0]})"
            )
        # 통과 — generic_visit 불필요 (ImportFrom 하위에는 검사할 노드 없음)

    def visit_Call(self, node):
        if isinstance(node.func, ast.Name) and node.func.id in FORBIDDEN_FUNCTIONS:
            raise Exception(f"금지 함수 사용: {node.func.id}")
        self.generic_visit(node)


def _is_count_call(call_node: ast.AST) -> bool:
    """
    `for ... in count(...)` 또는 `for ... in itertools.count(...)` 의
    iter 부분이 itertools.count 호출인지 판별합니다.
    """
    if not isinstance(call_node, ast.Call):
        return False
    f = call_node.func
    if isinstance(f, ast.Name) and f.id == "count":
        return True
    if (isinstance(f, ast.Attribute)
            and isinstance(f.value, ast.Name)
            and f.value.id == "itertools"
            and f.attr == "count"):
        return True
    return False


class LoopTransformer(ast.NodeTransformer):
    def visit_While(self, node):
    # 조건이 무엇이든(while True든 while resCommon < 100이든) 무조건 제어
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

    # 매 반복 틱마다 제어권을 Unity(클라이언트)로 넘겨주도록 yield 삽입
        node.body.append(ast.Expr(value=ast.Yield(value=None)))
        return node

    def visit_For(self, node):
        # `for i in count(...)` / `for i in itertools.count(...)` 무한 for 처리.
        # while True 와 동일하게 매 반복 끝에 yield 를 삽입해 generator 화 합니다.
        if _is_count_call(node.iter):
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

        # ✨ [수정] 채굴 함수: 지정된 타겟의 변수를 실시간으로 증가
        def _mining(target="resCommon", amount=1):
            print(f"[ACTION] MINING_{str(target).upper()}")
            var_name = _get_var_name(target)
            # 샌드박스 환경(env)에 변수가 있으면 더하고, 없으면 새로 만듭니다
            if var_name in self.env:
                self.env[var_name] += amount
            else:
                self.env[var_name] = amount
            
        # ✨ [수정] 생산/가공 함수: producting도 동일하게 변수 실시간 증가
        def _producting(target="Standard", amount=1):
            print(f"[ACTION] PRODUCTING_{str(target).upper()}")
            var_name = _get_var_name(target)
            if var_name in self.env:
                self.env[var_name] += amount
            else:
                self.env[var_name] = amount

        # ✨ [보너스] 판매 함수: 판매 동작을 하면 골드(Gold) 변수가 증가
        def _selling(target="resCommon", amount=1):
            print(f"[ACTION] SELLING_{str(target).upper()}")
            self.env["Gold"] += 10  # 골드 10 획득 (원하는 수치로 조정 가능)

        def _move(speed="slow"):
            if speed == "fast":
                print("[ACTION] MOVE_FAST")
            else:
                print("[ACTION] MOVE")

        self.env = {
            "__builtins__": {
                "print": print, "range": range, "len": len,
                "int": int, "float": float, "str": str, "bool": bool,
                "__import__": _safe_import,
            },
            "mining": _mining,
            "producting": _producting,
            "processing": lambda target="Standard": _producting(target), # 편의상 producting과 동일하게 처리
            "machining": lambda target="Standard": _producting(target),
            "storing": lambda target="Standard": print(f"[ACTION] STORING_{str(target).upper()}"),
            "selling": _selling,
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
        res = cursor.fetchone() or {"resource_1":0, "resource_2":0, "resource_3":0, "resource_4":300, "resource_5":0, "total_play_time":0, "expand_count":0, "quest_id":0, "tutorial_step":0, "conveyor_level":0}
        
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

# 1. 리더보드 정보 가져오기 API
@app.get("/get_leaderboard")
def get_leaderboard():
    conn = get_db_connection()
    cursor = conn.cursor(dictionary=True)
    try:
        # 1. 추천수 Top 5 (기존 유지)
        cursor.execute("SELECT id, recommend_count FROM users ORDER BY recommend_count DESC LIMIT 5")
        top_recommends = cursor.fetchall()
        
        # 2. 골드 Top 5 (game_saves의 resource_4를 JOIN으로 가져오기)
        # u(users)의 ID와 g(game_saves)의 resource_4를 매칭하여 정렬합니다.
        cursor.execute("""
            SELECT u.id, g.resource_4 as total_gold 
            FROM game_saves g
            JOIN users u ON g.user_pk = u.pk_id
            ORDER BY g.resource_4 DESC 
            LIMIT 5
        """)
        top_golds = cursor.fetchall()
        
        return {"status": "SUCCESS", "top_recommends": top_recommends, "top_golds": top_golds}
    except Exception as e:
        return {"status": "ERROR", "msg": str(e)}
    finally:
        conn.close()

# 2. 유저 추천하기 API (놀러가기 화면용)
@app.post("/recommend_user")
def recommend_user(req: RecommendRequest):
    if req.from_user_id == req.to_user_id:
        return {"status": "FAIL", "msg": "자기 자신은 추천할 수 없습니다."}

    conn = get_db_connection()
    cursor = conn.cursor()
    try:
        # 이미 추천했는지 확인
        cursor.execute("SELECT id FROM user_recommends WHERE from_user_id = %s AND to_user_id = %s", (req.from_user_id, req.to_user_id))
        if cursor.fetchone():
            return {"status": "FAIL", "msg": "이미 추천한 유저입니다."}

        # 추천 로그 기록
        cursor.execute("INSERT INTO user_recommends (from_user_id, to_user_id) VALUES (%s, %s)", (req.from_user_id, req.to_user_id))
        
        # 타겟 유저의 추천수 + 1
        cursor.execute("UPDATE users SET recommend_count = recommend_count + 1 WHERE id = %s", (req.to_user_id,))
        conn.commit()

        # 업데이트된 최신 추천수 반환
        cursor.execute("SELECT recommend_count FROM users WHERE id = %s", (req.to_user_id,))
        new_count = cursor.fetchone()[0]

        return {"status": "SUCCESS", "msg": "추천 완료!", "new_count": new_count}
    except Exception as e:
        conn.rollback()
        return {"status": "ERROR", "msg": str(e)}
    finally:
        conn.close()

# 3. 특정 유저 추천수 조회 API (인게임 및 놀러가기 화면용)
@app.get("/get_recommend_count")
def get_recommend_count(user_id: str):
    conn = get_db_connection()
    cursor = conn.cursor()
    try:
        cursor.execute("SELECT recommend_count FROM users WHERE id = %s", (user_id,))
        result = cursor.fetchone()
        count = result[0] if result else 0
        return {"status": "SUCCESS", "recommend_count": count}
    finally:
        conn.close()

# 4. 특정 유저의 골드 및 추천 랭킹(순위) 조회 API
@app.get("/get_user_rankings")
def get_user_rankings(user_id: str):
    conn = get_db_connection()
    cursor = conn.cursor(dictionary=True)
    try:
        user_pk = get_user_pk(cursor, user_id)
        if not user_pk:
            return {"status": "ERROR", "msg": "유저 없음"}

        # 1. 내 추천수 및 순위 조회
        cursor.execute("SELECT recommend_count FROM users WHERE id = %s", (user_id,))
        my_rec = cursor.fetchone()['recommend_count']
        cursor.execute("SELECT COUNT(*) + 1 as my_rank FROM users WHERE recommend_count > %s", (my_rec,))
        rec_rank = cursor.fetchone()['my_rank']

        # 2. 내 골드 및 순위 조회
        cursor.execute("SELECT resource_4 FROM game_saves WHERE user_pk = %s", (user_pk,))
        gold_row = cursor.fetchone()
        my_gold = gold_row['resource_4'] if gold_row else 0
        cursor.execute("SELECT COUNT(*) + 1 as my_rank FROM game_saves WHERE resource_4 > %s", (my_gold,))
        gold_rank = cursor.fetchone()['my_rank']

        return {
            "status": "SUCCESS", 
            "recommend_rank": rec_rank, 
            "gold_rank": gold_rank,
            "my_recommend": my_rec,
            "my_gold": my_gold
        }
    except Exception as e:
        return {"status": "ERROR", "msg": str(e)}
    finally:
        conn.close()

@app.delete("/delete/game")
async def delete_game_data(user_id: str):
    conn = get_db_connection() #
    cursor = conn.cursor() #
    try:
        user_pk = get_user_pk(cursor, user_id) #
        if not user_pk: 
            return {"status": "FAIL", "msg": "User not found"}
        
        cursor.execute("DELETE FROM game_saves WHERE user_pk = %s", (user_pk,))
        cursor.execute("DELETE FROM installed_machines WHERE user_pk = %s", (user_pk,))
        cursor.execute("DELETE FROM code_logs WHERE user_pk = %s", (user_pk,))
        
        conn.commit()
        return {"status": "SUCCESS"}
    except Exception as e:
        conn.rollback()
        return {"status": "ERROR", "msg": str(e)}
    finally:
        conn.close()

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8000)