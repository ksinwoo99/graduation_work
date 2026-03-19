import ast

# 특징 추출 함수
def extract_features(source_code):
    features = {'for_count': 0, 'while_count': 0, 'if_count': 0, 'switch_count': 0, 
                'ternary_count': 0, 'func_call_count': 0, 'assign_count': 0, 'line_count': 0}
    
    # 비어있거나 None일 경우
    if not source_code: 
        print("소스 코드가 비어있습니다.")
        return features

    # 코드의 앞 뒤 공백을 제거한 후 줄바꿈 문자를 기준으로 전체 라인 수 계산
    features['line_count'] = len(source_code.strip().split('\n'))

    try:
        # 문자열을 추상 트리 구조로 변환
        tree = ast.parse(source_code)

        # 순회하며 각 노드의 타입을 확인하여 카운트
        for node in ast.walk(tree):
            if isinstance(node, ast.For): features['for_count'] += 1
            elif isinstance(node, ast.While): features['while_count'] += 1
            elif isinstance(node, ast.If): features['if_count'] += 1
            elif isinstance(node, ast.IfExp): features['ternary_count'] += 1
            elif hasattr(ast, 'Match') and isinstance(node, getattr(ast, 'Match')): features['switch_count'] += 1
            elif isinstance(node, ast.Call): features['func_call_count'] += 1
            elif isinstance(node, ast.Assign): features['assign_count'] += 1
    except SyntaxError: 
        print("특징 추출 실패 : 트리 문법 오류가 발생했을 때 나오는 메시지입니다.")
        
    return features