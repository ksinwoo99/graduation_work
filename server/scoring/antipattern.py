"""
server/scoring/antipattern.py
─────────────────────────────────────────────────────────────
AST 기반 결정론 안티패턴 감지 + 페널티 산출.

extract_features() 가 다루지 않는 "나쁜 코드 습관" 을 탐지해
교육 목표에 정렬된 감점을 부여합니다.

탐지 항목 / 가중치:
    dead_code          15  return/break/continue 다음 줄에 코드가 더 있음
    over_nesting       10  제어 흐름 최대 중첩 깊이 ≥ 4
    infinite_no_break  10  while True 안에 break 가 없음
    unused_variable     8  할당했지만 어디에서도 사용하지 않은 변수
    magic_range         7  range(threshold 이상) 사용 (의미 없는 큰 숫자)
    error_recurrence   20  직전 제출과 같은 종류의 에러 재발 (별도 함수)

문법 오류 코드는 0 / [] 반환 (감점 중복 방지).

NOTE: 과거에 존재하던 `duplicate_lines` (같은 실행문 N회 반복 감점) 은
      mining() / producting() 같은 머신 함수를 디버깅 목적으로 여러 번 호출한
      정상 코드까지 감점하는 부작용이 있어 제거되었습니다. 머신 함수는
      스펙상 한 번만 호출하면 충분하지만, 그 외의 호출 횟수를 채점에서
      가/감점하지 않습니다.
"""

import ast


PENALTY_WEIGHTS: dict[str, int] = {
    "dead_code":          15,
    "over_nesting":       10,
    "infinite_no_break":  10,
    "unused_variable":     8,
    "magic_range":         7,
    "error_recurrence":   20,
}

# over_nesting 임계값 — 제어 흐름 최대 중첩 깊이가 이 이상이면 감점
_NESTING_THRESHOLD   = 4
# magic_range 임계값 — range 인자가 이 값 이상이면 무의미하게 큰 숫자로 간주
_MAGIC_RANGE_THRESHOLD = 1000


# ──────────────────────────────────────────────
# 개별 안티패턴 감지 헬퍼
# ──────────────────────────────────────────────

_TERMINATING = (ast.Return, ast.Break, ast.Continue, ast.Raise)


def _has_dead_code(tree: ast.AST) -> bool:
    """
    return / break / continue / raise 다음 줄에
    같은 블록에서 실행될 코드가 남아 있는지 확인합니다.
    if/for/while 의 else / orelse 블록까지 모두 검사합니다.
    """
    for node in ast.walk(tree):
        for block_name in ("body", "orelse"):
            block = getattr(node, block_name, None)
            if not isinstance(block, list):
                continue
            for stmt in block[:-1]:
                if isinstance(stmt, _TERMINATING):
                    return True
    return False


_DEPTH_NODES = (ast.For, ast.While, ast.If, ast.Try, ast.With)


def _max_depth(tree: ast.AST) -> int:
    """제어 흐름의 최대 중첩 깊이."""
    def _depth(node: ast.AST, current: int = 0) -> int:
        if isinstance(node, _DEPTH_NODES):
            current += 1
        max_d = current
        for child in ast.iter_child_nodes(node):
            max_d = max(max_d, _depth(child, current))
        return max_d
    return _depth(tree)


def _is_infinite_while(node: ast.While) -> bool:
    """while True / while 1 패턴."""
    test = node.test
    return isinstance(test, ast.Constant) and test.value in (True, 1)


def _has_unused_assign(tree: ast.AST) -> bool:
    """
    할당된 변수 이름 중 어느 곳에서도 Load 컨텍스트로 참조되지 않은 변수가 있으면 True.
    함수 인자/속성 접근 등은 제외, 단일 토큰 변수 할당만 검사.
    """
    assigned: set[str] = set()
    used:     set[str] = set()
    for node in ast.walk(tree):
        if isinstance(node, ast.Assign):
            for tgt in node.targets:
                if isinstance(tgt, ast.Name):
                    # 언더스코어로 시작하는 이름(_, _foo)은 의도적 미사용으로 간주
                    if not tgt.id.startswith("_"):
                        assigned.add(tgt.id)
        elif isinstance(node, ast.Name) and isinstance(node.ctx, ast.Load):
            used.add(node.id)
    return bool(assigned - used)


def _has_magic_range(tree: ast.AST, threshold: int) -> bool:
    """range(N) 의 N 이 threshold 이상이면 True."""
    for node in ast.walk(tree):
        if (isinstance(node, ast.Call)
                and isinstance(node.func, ast.Name)
                and node.func.id == "range"):
            for arg in node.args:
                try:
                    val = ast.literal_eval(arg)
                except (ValueError, TypeError):
                    continue
                if isinstance(val, int) and abs(val) >= threshold:
                    return True
    return False


# ──────────────────────────────────────────────
# Public API
# ──────────────────────────────────────────────

def antipattern_penalty(source: str) -> tuple[float, list[str]]:
    """
    소스 코드에서 안티패턴을 감지해 (총 감점, 태그 리스트) 를 반환합니다.

    Args:
        source : 사용자 코드 원본 (str)

    Returns:
        (total_penalty, tags)
            total_penalty : float, 0.0 이상
            tags          : 감지된 안티패턴 태그 list[str]
    """
    if not source or not source.strip():
        return 0.0, []

    try:
        tree = ast.parse(source)
    except SyntaxError:
        # 문법 오류는 별도 시그널(에러 힌트) 로 처리되므로 감점 중복 방지
        return 0.0, []

    tags: list[str] = []
    total = 0.0

    if _has_dead_code(tree):
        total += PENALTY_WEIGHTS["dead_code"]
        tags.append("dead_code")

    if _max_depth(tree) >= _NESTING_THRESHOLD:
        total += PENALTY_WEIGHTS["over_nesting"]
        tags.append("over_nesting")

    for node in ast.walk(tree):
        if isinstance(node, ast.While) and _is_infinite_while(node):
            has_break = any(
                isinstance(c, ast.Break) for c in ast.walk(node)
            )
            if not has_break:
                total += PENALTY_WEIGHTS["infinite_no_break"]
                tags.append("infinite_no_break")
                break

    if _has_unused_assign(tree):
        total += PENALTY_WEIGHTS["unused_variable"]
        tags.append("unused_variable")

    if _has_magic_range(tree, threshold=_MAGIC_RANGE_THRESHOLD):
        total += PENALTY_WEIGHTS["magic_range"]
        tags.append("magic_range")

    return total, tags


def error_recurrence_penalty(cur_error: str | None, prev_error: str | None) -> float:
    """
    직전 제출과 같은 종류의 에러가 또 발생했다면
    "힌트 무시" 로 간주하여 가장 큰 감점(20)을 부여합니다.

    cur_error / prev_error 는 _extract_error_type() 결과 (예: "nameerror").
    둘 중 하나라도 None 이면 0.0.
    """
    if not cur_error or not prev_error:
        return 0.0
    if cur_error == prev_error:
        return float(PENALTY_WEIGHTS["error_recurrence"])
    return 0.0
