"""
user_messages.py — 사용자에게 표시되는 모든 문구 (단일 수정 지점)

AI 힌트, 파이썬/게임 오류 안내, 성장·정체 메모, API 응답 message 등
플레이어·클라이언트에 노출되는 한국어 문구는 이 파일만 편집하면 됩니다.

사용법 (main.py):
    from user_messages import HINT_VARIANTS, RANK_LABELS, msg, Err, Moving, ...

    return msg(Err.NAME_GENERIC)
    return msg(Err.NAME_GENERIC)
    return msg(Moving.TYPO, token=token)   # {token} 치환
"""

from __future__ import annotations


# ══════════════════════════════════════════════════════════════════════════════
# 성공 힌트 — 밴딧 변형 풀 (hint_text, hint_type_id)
# ══════════════════════════════════════════════════════════════════════════════

HINT_VARIANTS: dict[str, list[tuple[str, str]]] = {
    "succ_r0_simple": [
        (
            "[ 단순 코드형 ] "
            "명령을 하나씩 순서대로 실행하는 코드예요. "
            "반복문(for)을 사용하면 같은 명령을 여러 번 한 번에 실행할 수 있어요! "
            "예시: for i in range(5): mining()",
            "succ_r0_simple_A",
        ),
        (
            "[ 단순 코드형 ] "
            "자원 함수를 한 줄씩 쓰는 대신 반복문으로 묶어보세요! "
            "예시: for i in range(5): mining()",
            "succ_r0_simple_B",
        ),
    ],
    "succ_r0_has_if": [
        (
            "[ 단순 코드형 ] "
            "조건문(if)을 활용하고 있어요! "
            "여기에 반복문(for)까지 더하면 훨씬 강력해집니다. "
            "예시: for i in range(5): mining()",
            "succ_r0_has_if_A",
        ),
        (
            "[ 단순 코드형 ] "
            "if 판단을 잘 쓰고 있어요! "
            "while 문으로 기계를 계속 돌리면서 if 로 상황을 판단하면 더 강력해요.",
            "succ_r0_has_if_B",
        ),
    ],
    "succ_r1_for": [
        (
            "[ 일반 학습자형 ] "
            "for 반복문을 잘 쓰고 있어요! "
            "range() 의 숫자를 더 키우거나, while True: + break 조건으로 자동화에 도전해보세요.",
            "succ_r1_for_A",
        ),
        (
            "[ 일반 학습자형 ] "
            "for 루프로 좋은 구조를 만들었어요! "
            "for i in count(): 으로 끝나지 않는 무한 반복도 도전해보세요.",
            "succ_r1_for_B",
        ),
    ],
    "succ_r1_while": [
        (
            "[ 일반 학습자형 ] "
            "while 반복문을 사용하고 있어요! "
            "while True: 로 변경하면 기계가 멈추지 않고 계속 자동으로 작동해요.",
            "succ_r1_while_A",
        ),
        (
            "[ 일반 학습자형 ] "
            "while 루프를 쓰고 있군요! "
            "조건문 대신 while True: + 내부 break 로 더 명확한 종료 흐름을 만들 수 있어요.",
            "succ_r1_while_B",
        ),
    ],
    "succ_r2_infinite_while": [
        (
            "[ 효율 최적화형 ] "
            "while True: 로 기계를 완전 자동화했어요! "
            "내부에 if + break 종료 조건이 있으면 더 안전한 코드가 됩니다.",
            "succ_r2_infinite_while_A",
        ),
        (
            "[ 효율 최적화형 ] "
            "무한 루프로 완벽한 자동화 코드예요! "
            "자원량을 확인해서 멈추는 종료 조건을 추가하면 한층 견고해집니다.",
            "succ_r2_infinite_while_B",
        ),
    ],
    "succ_r2_infinite_for_count": [
        (
            "[ 효율 최적화형 ] "
            "for i in count(): 로 깔끔한 무한 반복을 구현했네요! "
            "i 값을 활용해 단계별 동작을 분기하면 표현력이 훨씬 풍부해져요.",
            "succ_r2_infinite_for_count_A",
        ),
        (
            "[ 효율 최적화형 ] "
            "for i in count(): 가 들어간 깔끔한 무한 자동화예요! "
            "좋은 코드입니다. break 조건만 챙겨주세요.",
            "succ_r2_infinite_for_count_B",
        ),
    ],
    "succ_r2_for_range": [
        (
            "[ 효율 최적화형 ] "
            "큰 횟수의 for range 로 고효율 작업 코드를 만들었어요! "
            "더 나아가 while True: 나 for i in count(): 으로 완전 자동화도 가능합니다.",
            "succ_r2_for_range_A",
        ),
        (
            "[ 효율 최적화형 ] "
            "효율적인 for range 루프예요! "
            "기계를 멈추지 않게 하려면 for i in count(): 처럼 끝이 정해지지 않는 루프를 시도해보세요.",
            "succ_r2_for_range_B",
        ),
    ],
    # ── rank 1: 이미 무한 루프 — upsell 없음 ──
    "succ_r1_mastery": [
        (
            "[ 일반 학습자형 ] "
            "무한 자동화 코드를 잘 작성했어요! "
            "다른 등급 기계에도 for / while 을 골고루 써서 균형을 맞춰보세요.",
            "succ_r1_mastery_A",
        ),
        (
            "[ 일반 학습자형 ] "
            "자동화에 성공했어요! "
            "루프 안에 if 로 자원량을 확인하면 더 영리한 코드가 됩니다.",
            "succ_r1_mastery_B",
        ),
    ],
    # ── rank 2: count 무한 for — 심화 / 천장 ──
    "succ_r2_count_if_mastery": [
        (
            "[ 효율 최적화형 ] "
            "for i in count() 와 if 분기를 함께 쓴 고급 자동화예요! "
            "i 값으로 단계별 동작을 나누면 표현력이 더 풍부해집니다.",
            "succ_r2_count_if_mastery_A",
        ),
        (
            "[ 효율 최적화형 ] "
            "count 무한 루프 + 조건 분기 — 훌륭합니다. "
            "다른 기계에도 같은 패턴을 적용해보세요.",
            "succ_r2_count_if_mastery_B",
        ),
    ],
    "succ_r2_count_add_if": [
        (
            "[ 효율 최적화형 ] "
            "for i in count() 자동화 성공! "
            "루프 안에 if resCommon >= N: 분기를 넣으면 상황별 생산이 가능해요.",
            "succ_r2_count_add_if_A",
        ),
    ],
    "succ_r2_count_add_break": [
        (
            "[ 효율 최적화형 ] "
            "for i in count() 로 잘 돌리고 있어요! "
            "자원이 부족할 때 break 로 멈추면 더 안전한 코드가 됩니다.",
            "succ_r2_count_add_break_A",
        ),
    ],
    "succ_r2_count_ceiling": [
        (
            "[ 효율 최적화형 ] "
            "count 무한 루프 자동화를 완성했어요! "
            "이제 다른 등급 기계 코드도 점검해 보세요.",
            "succ_r2_count_ceiling_A",
        ),
    ],
    # ── rank 2: while True — break / if 심화 ──
    "succ_r2_while_add_break": [
        (
            "[ 효율 최적화형 ] "
            "while True 자동화 중이에요! "
            "break 조건을 넣으면 예기치 않은 상황에서도 안전합니다.",
            "succ_r2_while_add_break_A",
        ),
        (
            "[ 효율 최적화형 ] "
            "무한 while 루프 — break 로 종료 조건을 추가해보세요. "
            "예) if resCommon < 10: break",
            "succ_r2_while_add_break_B",
        ),
    ],
    "succ_r2_while_add_if": [
        (
            "[ 효율 최적화형 ] "
            "while True 에 if resCommon >= N: 같은 조건 분기를 추가하면 "
            "더 영리한 자동화가 됩니다.",
            "succ_r2_while_add_if_A",
        ),
    ],
    "succ_r2_while_mastery": [
        (
            "[ 효율 최적화형 ] "
            "while True + break + if 까지 갖춘 견고한 자동화 코드예요! "
            "훌륭합니다.",
            "succ_r2_while_mastery_A",
        ),
    ],
    # ── 필터 후보 없을 때 공통 fallback ──
    "succ_r2_ceiling": [
        (
            "[ 효율 최적화형 ] "
            "훌륭한 코드입니다! "
            "다른 기계에서도 다양한 반복문을 골고루 써보세요.",
            "succ_r2_ceiling_A",
        ),
    ],
}

HINT_VARIANTS_MAP: dict[str, str] = {
    hint_type: text
    for variants in HINT_VARIANTS.values()
    for (text, hint_type) in variants
}


# ══════════════════════════════════════════════════════════════════════════════
# 군집 / 성장·정체 메모
# ══════════════════════════════════════════════════════════════════════════════

RANK_LABELS: dict[int, str] = {
    0: "단순 코드형",
    1: "일반 학습자형",
    2: "효율 최적화형",
}

RANK_LABEL_UNKNOWN = "알 수 없음"

STAGNATION_NUDGE: dict[int, str] = {
    0: "for i in range(5): mining() 처럼 반복문을 시작해보세요!",
    1: "while True, for i in count() 로 무한 반복에 도전해보세요!",
}

STAGNATION_NUDGE_DEFAULT = "새로운 방식을 시도해보세요."

# {prev_label}, {cur_label}, {consecutive}
PROGRESSION_GROWTH = (
    "\n[ 성장 중! ] {prev_label}에서 {cur_label}으로 올라섰어요! "
    "이 방향으로 계속 나아가세요."
)
PROGRESSION_DECLINE_FROM_RANK2 = (
    "\n[ 효율 하락 ] 이전에는 {prev_label}이었어요. "
    "반복문(for/while)을 더 적극적으로 활용해보세요!"
)
PROGRESSION_DECLINE_GENERIC = (
    "\n[ 패턴 단순화 ] 이전보다 코드 구조가 단순해졌어요. "
    "반복문을 계속 활용해보세요!"
)
PROGRESSION_STAGNATION = (
    "\n[ {cur_label} 유지 중 ] "
    "{consecutive}번 연속 같은 패턴이에요. {nudge}"
)


# ══════════════════════════════════════════════════════════════════════════════
# 밴딧 / 성공 고정 힌트
# ══════════════════════════════════════════════════════════════════════════════

BANDIT_FALLBACK_OK = "코드가 정상 적용되었습니다."

SUCCESS_UNKNOWN_CLUSTER = (
    "코드가 정상 적용되었습니다. 반복문을 활용하면 더 높은 점수를 받을 수 있어요!"
)

SUCCESS_MOVING_STANDALONE = (
    "[ 컨테이너 타일 ] "
    "moving() 명령으로 컨테이너 타일을 설치했어요! "
    "단독 호출 전용 명령이라 100점 처리됩니다."
)


# ══════════════════════════════════════════════════════════════════════════════
# moving() 전용 (Layer 0)
# ══════════════════════════════════════════════════════════════════════════════

class Moving:
    UNCLOSED_PAREN = (
        "'moving(' 의 닫는 괄호 ')' 가 빠진 것 같아요!\n"
        "컨테이너 타일은 'moving()' 처럼 빈 괄호로 정확히 입력해야 해요."
    )
    TYPO = (
        "'{token}' 은(는) 'moving()' 의 오타로 보여요!\n"
        "컨테이너 타일을 설치하려면 정확히 'moving()' 라고 입력해주세요."
    )
    IN_LOOP = (
        "moving() 는 컨테이너 타일을 설치하는 단독 명령어예요!\n"
        "for / while 반복문 안에서는 사용할 수 없어요."
    )


# ══════════════════════════════════════════════════════════════════════════════
# 파이썬 / 샌드박스 오류 힌트 (1단계)
# ══════════════════════════════════════════════════════════════════════════════

class Err:
    # 샌드박스
    SANDBOX_IMPORT_MODULE = (
        "'{target}' 모듈은 사용할 수 없어요.\n"
        "이 게임에서는 외부 모듈 import 를 쓸 수 없어요."
    )
    SANDBOX_IMPORT_GENERIC = (
        "외부 모듈 import 는 사용할 수 없어요!\n"
        "게임 명령어와 기본 파이썬만 사용할 수 있습니다."
    )
    SANDBOX_FORBIDDEN_FN = (
        "'{target}' 는 보안상 사용할 수 없는 함수예요.\n"
        "다른 방법으로 동일한 동작을 만들어보세요."
    )
    SANDBOX_FORBIDDEN_FN_FALLBACK_TARGET = "해당 함수"

    # SyntaxError
    SYNTAX_INDENT_BLOCK_LINE = (
        "'{problem_line}' 아래에 실행할 코드가 없어요!\n"
        "콜론(:) 다음 줄을 4칸 들여쓰기 후 명령어를 써주세요."
    )
    SYNTAX_INDENT_BLOCK = (
        "콜론(:) 뒤에 실행할 코드 블록이 없어요.\n"
        "들여쓰기(4칸) 후 명령어를 추가해보세요."
    )
    SYNTAX_UNEXPECTED_INDENT = (
        "들여쓰기가 필요 없는 곳에 빈칸이 들어가 있어요!\n"
        "코드 앞의 불필요한 공백을 지워주세요."
    )
    SYNTAX_TAB_MIX = (
        "탭(Tab)과 스페이스를 함께 쓰면 안 돼요!\n"
        "들여쓰기를 모두 스페이스 4칸으로 통일해보세요."
    )
    SYNTAX_UNCLOSED_PAREN = "괄호 '(' 또는 '[' 를 열고 닫지 않았는지 확인해보세요!"
    SYNTAX_UNCLOSED_STRING = "따옴표('' 또는 \"\")를 열고 닫지 않았는지 확인해보세요!"
    SYNTAX_RETURN_OUTSIDE = (
        "return 은 def 로 만든 함수 안에서만 쓸 수 있어요!\n"
        "함수 정의(def) 없이 return 만 쓰지는 않았나요?"
    )
    SYNTAX_BREAK_OUTSIDE = "break 는 for / while 반복문 안에서만 쓸 수 있어요!"
    SYNTAX_CONTINUE_OUTSIDE = "continue 는 for / while 반복문 안에서만 쓸 수 있어요!"
    SYNTAX_ASSIGN_VS_COMPARE = (
        "조건식에서는 비교 연산자 == 을 써야 해요.\n"
        "대입(=)과 비교(==)를 헷갈린 건 아닌가요? 예) if a == 5:"
    )
    SYNTAX_INVALID_CHAR = (
        "코드에 보이지 않는 특수문자가 섞여 있어요!\n"
        "다른 곳에서 복사·붙여넣기 했다면 직접 다시 입력해보세요."
    )
    SYNTAX_FSTRING = (
        "f-string 문법 오류예요.\n"
        "f\"...{변수이름}...\" 형식인지, 중괄호 {} 가 제대로 닫혔는지 확인해보세요."
    )
    SYNTAX_GENERIC = (
        "명령어에 오타가 있거나, 조건문·반복문 뒤에 콜론(:)을 빠뜨렸을지도 몰라요!"
    )

    # NameError
    NAME_MACHINE_UNQUOTED = (
        "기계 이름 '{undef}' 을(를) 따옴표로 감싸지 않았어요!\n"
        'name = "{undef}" 처럼 수정해보세요.'
    )
    NAME_STRING_UNQUOTED = (
        "'{undef}' 을(를) 텍스트로 쓰려면 따옴표로 감싸야 해요!\n"
        '예시: 변수 = "{undef}"'
    )
    NAME_TYPO_SUGGEST = (
        "'{undef}' 를 찾을 수 없어요.\n"
        "{ctx} '{suggestion}' 의 오타는 아닌가요?"
    )
    CTX_GAME_CMD = "게임 명령어"
    CTX_PYTHON_BUILTIN = "파이썬 기본 명령어"
    NAME_GENERIC = (
        "존재하지 않는 명령어(또는 변수)를 불렀어요. 오타가 발생했는지 확인해보세요!"
    )

    # TypeError
    TYPE_STR_INT_CONCAT = (
        "문자열(글자)과 숫자를 바로 + 로 연결할 수 없어요!\n"
        'str(숫자) 로 변환 후 합쳐보세요. 예) "결과: " + str(5)'
    )
    TYPE_WRONG_ARGS_FN = (
        "'{fn_name}()' 에 잘못된 개수의 값을 넣었어요.\n"
        "괄호 안에 값이 필요 없는 명령어일 수도 있어요! 예) mining()"
    )
    TYPE_WRONG_ARGS_GENERIC = (
        "함수에 잘못된 개수의 인자를 전달했어요.\n"
        "괄호 안의 값 개수를 확인해보세요."
    )
    TYPE_NONE = (
        "결과값이 없는(None) 값을 사용하려 했어요.\n"
        "함수의 반환값이 있는지, 변수에 제대로 저장했는지 확인해보세요."
    )
    TYPE_NOT_SUBSCRIPTABLE = (
        "대괄호([])로 접근할 수 없는 값이에요.\n"
        "리스트(list)나 딕셔너리(dict)가 맞는지 확인해보세요."
    )
    TYPE_RANGE_NOT_INT = (
        "range() 안에는 정수(숫자)만 넣을 수 있어요.\n"
        "문자열이 들어가지는 않았나요? 예) range(5)"
    )
    TYPE_GENERIC = (
        "타입 에러가 발생했어요. 숫자가 들어갈 자리에 문자열(글자)을 넣지는 않았나요?"
    )

    # AttributeError
    ATTR_TYPO_SUGGEST = (
        "'{attr}' 를 찾을 수 없어요.\n"
        "{ctx} '{suggestion}' 의 오타는 아닌가요?"
    )
    ATTR_UNKNOWN = "'{attr}' 는 존재하지 않는 속성이에요. 오타인지 확인해보세요!"
    ATTR_GENERIC = (
        "존재하지 않는 속성이나 메서드를 불렀어요. 오타가 없는지 확인해보세요."
    )

    # ValueError
    VALUE_INT_LITERAL = (
        "숫자로 변환할 수 없는 값을 int()에 넣었어요.\n"
        "숫자로만 이루어진 문자열인지 확인해보세요. 예) int(\"123\")"
    )
    VALUE_GENERIC = (
        "명령어의 형식은 맞지만, 올바르지 않은 값이 들어갔어요.\n"
        "정확한 값을 입력했는지 확인해보세요."
    )

    # 기타 런타임
    ZERO_DIVISION = (
        "0으로 나누기를 시도했어요!\n"
        "나누는 수(분모)가 0이 되지 않도록 코드를 확인해보세요."
    )
    INDEX = (
        "리스트의 범위를 벗어난 위치에 접근했어요.\n"
        "인덱스 번호가 리스트 길이(len())를 넘지 않는지 확인해보세요."
    )
    KEY_WITH_NAME = (
        "딕셔너리에 {key_name} 키가 없어요!\n"
        "키 이름의 오타나 존재 여부를 확인해보세요."
    )
    KEY_GENERIC = (
        "딕셔너리에 없는 키에 접근했어요.\n"
        "키 이름의 오타나 존재 여부를 확인해보세요."
    )
    RECURSION = (
        "함수가 자기 자신을 너무 많이 호출했어요(재귀 깊이 초과)!\n"
        "함수 안에서 같은 함수를 계속 부르지는 않았나요?"
    )
    TIMEOUT = (
        "코드 실행 시간이 너무 오래 걸려요!\n"
        "끝나지 않는 무한 루프에 빠진 건 아닌지 확인해보세요."
    )
    UNKNOWN = (
        "기계가 미지의 파이썬 에러를 뿜어내고 있습니다.\n"
        "로그 창의 에러 메시지를 번역해서 문제를 해결해보세요!"
    )


# ══════════════════════════════════════════════════════════════════════════════
# 기계 조건 미충족 (2단계)
# ══════════════════════════════════════════════════════════════════════════════

class Machine:
    LOCKED_INFINITE = (
        "아직 '무한 루프' 시스템 권한이 잠겨 있어요!\n"
        "while True / for i in count(): 는 게임을 더 진행해 해금 후 사용 가능해요. "
        "지금은 for i in range(N): 으로 횟수 반복을 사용해보세요."
    )
    LOCKED_LOOP = (
        "아직 '반복문' 시스템 권한이 잠겨 있어요!\n"
        "퀘스트를 더 진행해 for / while 권한을 해금한 뒤 사용해보세요."
    )
    NO_NAME = (
        "기계를 작동시키려면 먼저 이름을 지어줘야 해요!\n"
        "코드 맨 윗줄에 name = \"이름\" 을 추가해보세요."
    )
    MISSING_FN = (
        "이 기계는 {fn} 명령어가 필요합니다.\n"
        "다른 명령어를 입력하지는 않았나요?"
    )
    WRONG_ARGS = (
        "이 기계 등급에 맞지 않는 함수 인자가 있어요.\n"
        "이 기계는 {expected} 형태만 사용할 수 있습니다."
    )
    GENERIC = "문법은 맞았지만, 이 기계가 수행할 수 없는 명령입니다."


# ══════════════════════════════════════════════════════════════════════════════
# HTTP / API 응답 (클라이언트·디버그 UI에 노출될 수 있음)
# ══════════════════════════════════════════════════════════════════════════════

class Api:
    USER_NOT_FOUND = "'{user_id}' 유저를 찾을 수 없습니다."
    LOG_NOT_FOUND = "log_id={log_id} 를 찾을 수 없습니다."
    CLUSTER_RANK_COLUMN_MISSING = (
        "cluster_rank 컬럼이 없습니다. "
        "ALTER TABLE code_logs ADD COLUMN cluster_rank INT DEFAULT -1; 를 실행하세요."
    )
    SCORING_V2_COLUMN_MISSING = (
        "Scoring 2.0 컬럼 없음. server/migrations/scoring_v2.sql 을 적용하세요."
    )
    CLUSTER_HISTORY_NO_DATA = "군집 예측이 기록된 성공 제출이 없습니다."
    LOOP_BALANCE_QUERY_FAIL = "설치 기계 코드 기록 조회에 실패했습니다."
    LOOP_BALANCE_NO_MACHINES = (
        "채굴기·가공기(8종)에 저장된 코드가 없습니다. "
        "각 기계 코드를 수정한 뒤 게임을 저장해 주세요."
    )
    LOOP_BALANCE_NO_LOOPS = "저장된 기계 코드에 반복문(for/while)이 없습니다."


# ══════════════════════════════════════════════════════════════════════════════
# 포맷 헬퍼
# ══════════════════════════════════════════════════════════════════════════════

def msg(template: str, **kwargs) -> str:
    """문구 템플릿에 값을 채워 반환합니다. 치환 키가 없으면 그대로 반환."""
    if not kwargs:
        return template
    try:
        return template.format(**kwargs)
    except KeyError:
        return template
