-- ================================================
-- SSMS에서 이 파일을 열고 F5로 실행하세요
-- ================================================

-- 1. DB 생성
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = N'대진포스DB')
    CREATE DATABASE 대진포스DB;
GO

USE 대진포스DB;
GO

-- 2. 메인 매출 테이블
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = '매출데이터')
CREATE TABLE 매출데이터 (
    Id          INT           IDENTITY(1,1) PRIMARY KEY,
    수집일시    DATETIME      DEFAULT GETDATE(),
    날짜        DATE          NULL,
    매장명      NVARCHAR(100) NOT NULL,
    중분류      NVARCHAR(100) NULL,
    메뉴명      NVARCHAR(200) NULL,
    메뉴코드    NVARCHAR(50)  NULL,
    판매수량    INT           NULL,
    서비스수량  INT           NULL,
    총수량      INT           NULL,
    총매출액    BIGINT        NULL,
    총할인액    BIGINT        NULL,
    평균매출액  BIGINT        NULL,
    매출비율    FLOAT         NULL
);
GO

-- 이미 테이블이 있을 경우 날짜 컬럼 추가 (없을 때만)
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('매출데이터') AND name = '날짜')
    ALTER TABLE 매출데이터 ADD 날짜 DATE NULL;
GO

-- 3. 조회용 인덱스
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_매출데이터_매장명_날짜')
    CREATE INDEX IX_매출데이터_매장명_날짜
        ON 매출데이터 (매장명, 날짜);
GO

-- 4. UPSERT(MERGE) 중복 방지용 computed column + 유니크 인덱스
--    날짜+매장명+중분류+메뉴명+메뉴코드 조합이 같으면 동일 행으로 취급해 덮어씌움
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('매출데이터') AND name = '_중분류키')
    ALTER TABLE 매출데이터 ADD _중분류키 AS ISNULL(중분류, '') PERSISTED;
GO
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('매출데이터') AND name = '_메뉴명키')
    ALTER TABLE 매출데이터 ADD _메뉴명키 AS ISNULL(메뉴명, '') PERSISTED;
GO
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('매출데이터') AND name = '_메뉴코드키')
    ALTER TABLE 매출데이터 ADD _메뉴코드키 AS ISNULL(메뉴코드, '') PERSISTED;
GO
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'UX_매출데이터_비즈니스키')
    CREATE UNIQUE INDEX UX_매출데이터_비즈니스키
        ON 매출데이터 (날짜, 매장명, _중분류키, _메뉴명키, _메뉴코드키);
GO

PRINT '✅ DB 및 테이블 생성 완료!';
GO
