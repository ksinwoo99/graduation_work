import ast

# while True / while 1 감지 시 loop_efficiency 에 부여하는 대표값
# calculate_score 공식 기준: min(10, 값 × 5) → 2.0 이면 최대 효율 보너스(+10) 획득
_INFINITE_WHILE_EFFICIENCY_PROXY = 2.0
_INFINITE_MASTERY_EFFICIENCY_BONUS = 0.15  # break / if in loop 시 loop_efficiency 가산

# _calculate_max_depth 에서 깊이 계산 대상이 되는 제어 흐름 노드 타입
_CONTROL_NODES = (ast.For, ast.While, ast.If, ast.Try, ast.With)


def _has_if_in_body(body: list) -> bool:
    for stmt in body:
        for node in ast.walk(stmt):
            if isinstance(node, ast.If):
                return True
    return False


def _has_break_in_body(body: list) -> bool:
    for stmt in body:
        for node in ast.walk(stmt):
            if isinstance(node, ast.Break):
                return True
    return False


def _uses_name_in_body(name: str, body: list) -> bool:
    for stmt in body:
        for node in ast.walk(stmt):
            if (isinstance(node, ast.Name)
                    and node.id == name
                    and isinstance(node.ctx, ast.Load)):
                return True
    return False


# ──────────────────────────────────────────────
# 내부 헬퍼: 무한 루프 while 판별
# ──────────────────────────────────────────────
def _is_infinite_while(node: ast.While) -> bool:
    """
    while True: 또는 while 1: 패턴을 감지합니다.
    Python 3.8+ 의 ast.Constant 기준으로 판별합니다.
    """
    test = node.test
    return isinstance(test, ast.Constant) and test.value in (True, 1)


def _is_count_call(call_node: ast.AST) -> bool:
    """
    `count(...)` 또는 `itertools.count(...)` 호출인지 판별합니다.
    `for i in count(...)` 무한 루프 감지용 (count 는 샌드박스 기본 제공).
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


def _is_infinite_for(node: ast.For) -> bool:
    """`for ... in count(...)` 또는 `for ... in itertools.count(...)` 무한 for 판별."""
    return _is_count_call(node.iter)


# ──────────────────────────────────────────────
# 내부 헬퍼: 제어 흐름 최대 중첩 깊이 계산
# ──────────────────────────────────────────────
def _calculate_max_depth(tree: ast.AST) -> int:
    """
    For / While / If / Try / With 제어 흐름 구조의 최대 중첩 깊이를 반환합니다.
    예) for 안에 if, 안에 while → 깊이 3
    """
    def _depth(node: ast.AST, current: int = 0) -> int:
        if isinstance(node, _CONTROL_NODES):
            current += 1
        max_d = current
        for child in ast.iter_child_nodes(node):
            max_d = max(max_d, _depth(child, current))
        return max_d

    return _depth(tree)


# ──────────────────────────────────────────────
# Public: 사이클로매틱 복잡도 기반 AST 복잡도 계산
# ──────────────────────────────────────────────
def calculate_ast_complexity(source_code: str) -> int:
    """
    코드의 복잡도를 정수로 반환합니다.

    계산 방식:
        복잡도 = (결정 포인트 수) + 1 + (최대 중첩 깊이)

    결정 포인트 = if / for / while / except 구문 수
                 + BoolOp(and / or) 의 피연산자 수 - 1

    파싱 불가(문법 오류) 시 -1 반환.
    빈 코드 시 0 반환.
    """
    if not source_code or not source_code.strip():
        return 0

    try:
        tree = ast.parse(source_code)

        decision_points = 0
        for node in ast.walk(tree):
            if isinstance(node, (ast.If, ast.For, ast.While, ast.ExceptHandler)):
                decision_points += 1
            elif isinstance(node, ast.BoolOp):
                # `a and b and c` → values 3개, 결정 포인트 2
                decision_points += len(node.values) - 1

        max_depth = _calculate_max_depth(tree)
        return decision_points + 1 + max_depth

    except SyntaxError:
        return -1


# ──────────────────────────────────────────────
# Public: ML 모델용 코드 특징 추출
# ──────────────────────────────────────────────
def extract_features(source_code: str) -> dict:
    """
    소스 코드에서 ML 학습 및 예측에 사용할 특징(Feature)을 추출합니다.

    반환 키 목록:
        기존: for_count, while_count, if_count, switch_count,
              ternary_count, func_call_count, assign_count, line_count
        신규: max_nesting_depth    - 제어 흐름 최대 중첩 깊이
              has_loop             - 루프 존재 여부 (0 or 1)
              loop_efficiency      - for range(N) 합계 / 전체 라인 수
                                     while True/1, for in count() 감지 시 PROXY 값으로 대체
              has_infinite_while   - while True / while 1 패턴 존재 여부 (0 or 1)
              has_infinite_for     - for ... in count(...) / itertools.count(...) 패턴 여부 (0 or 1)
              has_infinite_loop    - has_infinite_while OR has_infinite_for (0 or 1)
                                     게임 최종 목표(무한루프 자동화) 달성 여부 종합 피처
              uses_itertools       - for ... in count(...) 사용 (0 or 1) — 힌트 분기용
              has_if_inside_loop   - for/while 본문 안 if 존재 (0 or 1)
              has_break_in_loop    - for/while 본문 안 break 존재 (0 or 1)
              uses_loop_index      - for 타깃 변수(i 등)를 루프 본문에서 사용 (0 or 1)
              max_range_n          - range(N) 인자 중 최대 N (0 이면 없음)
    """
    features = {
        'for_count': 0,
        'while_count': 0,
        'if_count': 0,
        'switch_count': 0,
        'ternary_count': 0,
        'func_call_count': 0,
        'assign_count': 0,
        'line_count': 0,
        # ── 신규 특징 ──────────────────────────
        'max_nesting_depth': 0,
        'has_loop': 0,
        'loop_efficiency': 0.0,
        'has_infinite_while': 0,
        'has_infinite_for':   0,
        'has_infinite_loop':  0,
        'uses_itertools':     0,
        'has_if_inside_loop': 0,
        'has_break_in_loop':  0,
        'uses_loop_index':    0,
        'max_range_n':        0,
    }

    if not source_code:
        return features

    features['line_count'] = len(source_code.strip().split('\n'))

    try:
        tree = ast.parse(source_code)

        # for range(N) 누적 합산 (loop_efficiency 계산용)
        for_range_total = 0

        for node in ast.walk(tree):
            if isinstance(node, ast.For):
                features['for_count'] += 1
                if _has_if_in_body(node.body):
                    features['has_if_inside_loop'] = 1
                if _has_break_in_body(node.body):
                    features['has_break_in_loop'] = 1
                if isinstance(node.target, ast.Name):
                    if _uses_name_in_body(node.target.id, node.body):
                        features['uses_loop_index'] = 1
                # `for ... in count(...)` / `for ... in itertools.count(...)` → 무한 for
                if _is_infinite_for(node):
                    features['has_infinite_for'] = 1
                # range(N) 단일 인수 형태만 파싱 (range(5), range(10) 등)
                elif (
                    isinstance(node.iter, ast.Call)
                    and isinstance(node.iter.func, ast.Name)
                    and node.iter.func.id == 'range'
                    and len(node.iter.args) == 1
                ):
                    try:
                        n = ast.literal_eval(node.iter.args[0])
                        if isinstance(n, int) and n > 0:
                            for_range_total += n
                            features['max_range_n'] = max(features['max_range_n'], n)
                    except (ValueError, TypeError):
                        pass

            elif isinstance(node, ast.While):
                features['while_count'] += 1
                if _has_if_in_body(node.body):
                    features['has_if_inside_loop'] = 1
                if _has_break_in_body(node.body):
                    features['has_break_in_loop'] = 1
                if _is_infinite_while(node):
                    features['has_infinite_while'] = 1
            elif isinstance(node, ast.If):
                features['if_count'] += 1
            elif isinstance(node, ast.IfExp):
                features['ternary_count'] += 1
            elif hasattr(ast, 'Match') and isinstance(node, getattr(ast, 'Match')):
                features['switch_count'] += 1
            elif isinstance(node, ast.Call):
                features['func_call_count'] += 1
            elif isinstance(node, ast.Assign):
                features['assign_count'] += 1

        features['max_nesting_depth'] = _calculate_max_depth(tree)
        features['has_loop'] = int(
            (features['for_count'] + features['while_count']) > 0
        )
        features['has_infinite_loop'] = int(
            features['has_infinite_while'] or features['has_infinite_for']
        )
        if features['has_infinite_for']:
            features['uses_itertools'] = 1

        # 루프 효율성: for range(N) 총합 / 라인 수
        # 예) 4줄 코드에서 range(10) 사용 → 10 / 4 = 2.5
        if features['line_count'] > 0 and for_range_total > 0:
            features['loop_efficiency'] = round(
                for_range_total / features['line_count'], 4
            )

        # while True / for in count() — 무한 루프 프록시 (break·if in loop 시 가산)
        if features['has_infinite_loop']:
            proxy = _INFINITE_WHILE_EFFICIENCY_PROXY
            if features['has_if_inside_loop']:
                proxy += _INFINITE_MASTERY_EFFICIENCY_BONUS
            if features['has_break_in_loop']:
                proxy += _INFINITE_MASTERY_EFFICIENCY_BONUS
            if features['loop_efficiency'] < proxy:
                features['loop_efficiency'] = round(proxy, 4)

    except SyntaxError:
        pass   # 문법 오류 코드는 피처 기본값(0)으로 반환

    return features
