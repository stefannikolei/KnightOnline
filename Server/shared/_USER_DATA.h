#pragma once

#include "globals.h"

struct _ITEM_DATA
{
	int		nNum;		// item 번호 [Korean comment]
	short	sDuration;	// item 내구력 [Korean comment]
	short	sCount;		// item 갯수 or item 축복 속성에 해당 값
	BYTE	byFlag;
	WORD	sTimeRemaining;
	int64_t	nSerialNum;	// item serial code
};

struct _WAREHOUSE_ITEM_DATA
{
	int		nNum;		// item 번호 [Korean comment]
	short	sDuration;	// item 내구력 [Korean comment]
	short	sCount;		// item 갯수 or item 축복 속성에 해당 값
	int64_t	nSerialNum;	// item serial code
};

struct _USER_QUEST
{
	short	sQuestID;
	BYTE	byQuestState;
};

struct _USER_DATA
{
	char		m_id[MAX_ID_SIZE + 1];			// 유저 ID
	char		m_Accountid[MAX_ID_SIZE + 1];	// 계정 ID

	BYTE		m_bZone;						// 현재 Zone
	float		m_curx;							// 현재 X 좌표
	float		m_curz;							// 현재 Z 좌표
	float		m_cury;							// 현재 Y 좌표

	BYTE		m_bNation;						// 소속국가 [Korean comment]
	BYTE		m_bRace;						// 종족 [Korean comment]
	short		m_sClass;						// 직업 [Korean comment]
	BYTE		m_bHairColor;					// 머리색깔 Color
	BYTE		m_bRank;						// 작위 [Korean comment]
	BYTE		m_bTitle;						// 지위 [Korean comment]
	BYTE		m_bLevel;						// 레벨 [Korean comment]
	int			m_iExp;							// 경험치 [Korean comment]
	int			m_iLoyalty;						// 로열티 [Korean comment]
	int			m_iLoyaltyMonthly;				// 로열티 [Korean comment]
	BYTE		m_bFace;						// 얼굴모양 [Korean comment]
	BYTE		m_bCity;						// 소속도시 [Korean comment]
	short		m_bKnights;						// 소속 기사단 [Korean comment]
	BYTE		m_bFame;						// 명성 [Korean comment]
	short		m_sHp;							// HP
	short		m_sMp;							// MP
	short		m_sSp;							// SP
	BYTE		m_bStr;							// 힘 [Korean comment]
	BYTE		m_bSta;							// 생명력 [Korean comment]
	BYTE		m_bDex;							// 공격, 회피율 [Korean comment]
	BYTE		m_bIntel;						// 지혜(?), 캐릭터 마법력 결정 Character
	BYTE		m_bCha;							// 마법 성공률, 물건 가격 결정(?) [Korean comment]
	BYTE		m_bAuthority;					// 유저 권한 [Korean comment]
	BYTE		m_bPoints;						// 보너스 포인트 [Korean comment]
	int			m_iGold;						// 캐릭이 지닌 돈(21억) [Korean comment]
	short		m_sBind;						// Saved Bind Point
	int			m_iBank;						// 창고의 돈(21억) Window

	BYTE		m_bstrSkill[9];					// 직업별 스킬 [Korean comment]
	_ITEM_DATA	m_sItemArray[HAVE_MAX + SLOT_MAX];	// 42*8 bytes
	_WAREHOUSE_ITEM_DATA m_sWarehouseArray[WAREHOUSE_MAX];	// 창고 아이템	192*8 bytes

	BYTE		m_bLogout;						// 로그아웃 플래그 [Korean comment]
	BYTE		m_bWarehouse;					// 창고 거래 했었나? Window
	DWORD		m_dwTime;						// 플레이 타임... [Korean comment]

	BYTE		m_byPremiumType;
	short		m_sPremiumTime;
	int			m_iMannerPoint;

	short		m_sQuestCount;
	_USER_QUEST	m_quests[MAX_QUEST];
};

constexpr int ALLOCATED_USER_DATA_BLOCK = 8000;
static_assert(sizeof(_USER_DATA) <= ALLOCATED_USER_DATA_BLOCK);
