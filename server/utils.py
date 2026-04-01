import ast


# ──────────────────────────────────────────────
# 내부 헬퍼: 제어 흐름 최대 중첩 깊이 계산
# ──────────────────────────────────────────────
def _calculate_max_depth(tree: ast.AST) -> int:
    """
    For / While / If / Try / With 제어 흐름 구조의 최대 중첩 깊이를 반환합니다.
    예) for 안에 if, 안에 while → 깊이 3
    """
    CONTROL_NODES = (ast.For, ast.While, ast.If, ast.Try, ast.With)

    def _depth(node: ast.AST, current: int = 0) -> int:
        if isinstance(node, CONTROL_NODES):
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
        신규: max_nesting_depth  - 제어 흐름 최대 중첩 깊이
              has_loop           - 루프 존재 여부 (0 or 1)
              loop_efficiency    - for range(N) 합계 / 전체 라인 수
                                   (루프 1줄로 몇 번의 작업을 줄였는지 나타냄)
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
    }

    if not source_code:
        print("소스 코드가 비어있습니다.")
        return features

    features['line_count'] = len(source_code.strip().split('\n'))

    try:
        tree = ast.parse(source_code)

        # for range(N) 누적 합산 (loop_efficiency 계산용)
        for_range_total = 0

        for node in ast.walk(tree):
            if isinstance(node, ast.For):
                features['for_count'] += 1
                # range(N) 단일 인수 형태만 파싱 (range(5), range(10) 등)
                if (
                    isinstance(node.iter, ast.Call)
                    and isinstance(node.iter.func, ast.Name)
                    and node.iter.func.id == 'range'
                    and len(node.iter.args) == 1
                ):
                    try:
                        n = ast.literal_eval(node.iter.args[0])
                        if isinstance(n, int) and n > 0:
                            for_range_total += n
                    except (ValueError, TypeError):
                        pass

            elif isinstance(node, ast.While):
                features['while_count'] += 1
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
        features['has_loop'] = (
            1 if (features['for_count'] + features['while_count']) > 0 else 0
        )

        # 루프 효율성: for range(N) 총합 / 라인 수
        # 예) 4줄 코드에서 range(10) 사용 → 10 / 4 = 2.5
        if features['line_count'] > 0 and for_range_total > 0:
            features['loop_efficiency'] = round(
                for_range_total / features['line_count'], 4
            )

    except SyntaxError:
        print("특징 추출 실패 : 문법 오류가 있는 코드입니다.")

    return features
