const express = require('express');
const mysql = require('mysql2');
const app = express();

app.use(express.urlencoded({ extended: true }));
app.use(express.json());

const db = mysql.createConnection({
    host: 'localhost',
    user: 'root',
    password: '1234', 
    database: 'login_db' 
});

// 로그인
app.post('/login', (req, res) => {
    const { id, password } = req.body;
    db.query("SELECT * FROM users WHERE id = ? AND password = ?", [id, password], (err, results) => {
        if (err) res.send("ERROR");
        else if (results.length > 0) res.send("LOGIN_SUCCESS");
        else res.send("LOGIN_FAIL");
    });
});

// 아이디 중복 확인
app.post('/check_duplicate', (req, res) => {
    const { id } = req.body;
    db.query("SELECT * FROM users WHERE id = ?", [id], (err, results) => {
        if (err) res.send("ERROR");
        else if (results.length > 0) res.send("ID_EXIST"); // 이미 있음
        else res.send("ID_SAFE"); // 사용 가능
    });
});

// 회원가입
app.post('/register', (req, res) => {
    const { id, password } = req.body;
    
    db.query("INSERT INTO users (id, password) VALUES (?, ?)", [id, password], (err) => {
        if (err) res.send("ERROR");
        else res.send("REGISTER_SUCCESS");
    });
});

// 4. 비밀번호 찾기
app.post('/find_pw', (req, res) => {
    const { id } = req.body;
    db.query("SELECT password FROM users WHERE id = ?", [id], (err, results) => {
        if (err) res.send("ERROR");
        else if (results.length > 0) res.send(results[0].password); // 비밀번호 반환
        else res.send("USER_NOT_FOUND");
    });
});

app.listen(3000, () => console.log("서버 실행 중 (포트: 3000)"));