# 변경 기록

이 프로젝트는 [유의적 버전](https://semver.org/lang/ko/)을 따릅니다.

## [0.2.0] - 2026-11-18

### 변경
- 자동 한마디와 지금 말하기가 같은 모델 설정을 사용합니다. 메뉴는 **화면 보고 말하기** 하나로 합쳤고, 자동 실행 간격 설정으로 바로 갈 수 있습니다. 이전 설정은 지금 말하기에서 고른 모델을 이어받습니다.
- 소스를 `src/` 아래 기능별 폴더로 정리하고 설치 스크립트를 `installer/`로 옮겼습니다. 동작은 바뀌지 않았습니다.
- 배포본에서 한국어 외 프레임워크 리소스를 빼서 ZIP 크기를 줄였습니다.

### 수정
- `Install.cmd`로 설치할 때 바탕화면 바로 가기 이름(비서 펫)이 깨질 수 있던 문제를 고쳤습니다. Windows PowerShell 5.1이 BOM 없는 UTF-8 스크립트를 잘못 읽어서 생긴 문제입니다.
- 배포 ZIP 안의 경로 구분자를 `/`로 기록합니다.

### 프로젝트
- MIT 라이선스를 추가했습니다.
- GitHub Actions로 빌드, 자체 테스트, 설치 스크립트 검사를 자동 실행합니다. `v*` 태그를 올리면 릴리스가 자동으로 만들어집니다.
- Codex CLI가 설치되지 않은 PC에서도 자체 테스트를 실행할 수 있습니다. 실패하면 어떤 검사가 실패했는지 `self-test.json`에 남깁니다.

## [0.1.1] - 2026-09-23

### 수정
- 압축을 푼 폴더 이름에 대괄호가 있어도 설치할 수 있습니다.

## [0.1.0] - 2026-09-23

첫 공개 버전입니다.

[0.2.0]: https://github.com/danyoon0760/codex-secretary-overlay/compare/v0.1.1...v0.2.0
[0.1.1]: https://github.com/danyoon0760/codex-secretary-overlay/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/danyoon0760/codex-secretary-overlay/releases/tag/v0.1.0
