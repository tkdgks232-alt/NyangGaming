# Nyang Gaming · 냥게이밍

게임 활동과 전적, 친구 상태와 채팅을 한곳에서 확인하는 Windows 앱입니다.

## 설치

[최신 Setup 다운로드](https://github.com/tkdgks232-alt/NyangGaming/releases/latest/download/NyangGaming-win-Setup.exe)

1. 기존 ZIP 버전이 실행 중이면 트레이에서 **완전히 종료**합니다.
2. Setup을 최초 한 번 실행합니다. 기존 앱 제거나 사용자 데이터 삭제는 필요하지 않습니다.
3. 이후 설치된 Nyang Gaming 바로가기로 실행합니다. 새 버전은 앱 안에서 확인·다운로드·재시작하여 적용할 수 있습니다.

기존 설정·연결·개인 배경·플레이시간은 `%LOCALAPPDATA%\KoruGaming_Next\UserData`에 유지됩니다.

## 새 버전 배포

코드 변경을 커밋한 뒤 변경사항 메모 파일을 준비하고 실행합니다.

```powershell
.\Release.ps1 -Version 1.0.2 -NotesFile C:\경로\변경사항.txt
```

[Actions](https://github.com/tkdgks232-alt/NyangGaming/actions)에서 빌드 결과를 확인하세요. 성공하면 [Releases](https://github.com/tkdgks232-alt/NyangGaming/releases)에 자동 게시됩니다.
자세한 절차와 데이터 보존 구조는 [배포 안내](UPDATE-GUIDE.md)에 있습니다.

게임사 및 전적 사이트와 별개인 개인 프로젝트입니다.
