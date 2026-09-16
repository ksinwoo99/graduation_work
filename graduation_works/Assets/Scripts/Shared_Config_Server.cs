// 서버 주소 관리
public static class Shared_Config_Server {
    private const string HOST = "129.225.166.182";

    public const string GAME_SERVER_URL = "http://" + HOST + ":8000"; // 게임 데이터 저장/불러오기, 코드 실행
    public const string ML_SERVER_URL   = "http://" + HOST + ":8001"; // 힌트 추천, 점수 산정
    public const string AUTH_SERVER_URL = "http://" + HOST + ":8002"; // 회원가입, 로그인, 이메일 인증
}
