// SubProcPerTrade.h: interface for the CSubProcPerTrade class.
//
//////////////////////////////////////////////////////////////////////

#if !defined(AFX_SUBPROCPERTRADE_H__555E4865_7877_425D_8F29_DD028F251889__INCLUDED_)
#define AFX_SUBPROCPERTRADE_H__555E4865_7877_425D_8F29_DD028F251889__INCLUDED_

#if _MSC_VER > 1000
#pragma once
#endif // _MSC_VER > 1000

enum e_PerTradeState	{												// 아이템 개인 거래 상태.. Status
							PER_TRADE_STATE_NONE = 0,					// 아무것도 아님.. [Korean comment]
							PER_TRADE_STATE_WAIT_FOR_REQ,				// 상대방에게 요청하고 응답을 기다림.. [Korean comment]
							PER_TRADE_STATE_WAIT_FOR_MY_DECISION_AGREE_OR_DISAGREE,	// 상대방에게서 요청받고 내 결정을 기다림.. [Korean comment]
							PER_TRADE_STATE_NORMAL,						// 아이템 개인 거래 정상적인 상태.. Status
							PER_TRADE_STATE_ADD_AND_WAIT_FROM_SERVER,	// 아이템을 더하고 서버로 부터 응답을 기다림..	 [Korean comment]
							PER_TRARE_STATE_EDITTING,					// 아이템 개인 거래 금액이나 갯수등 편집중.. Edit
							PER_TRADE_STATE_MY_TRADE_DECISION_DONE,		// 내 거래 버튼 누른 상태..	 Button
						};

enum e_PerTradeResultCode	{										// 아이템 거래가 해제되는 코드 정의..								 [Korean comment]
								PER_TRADE_RESULT_MY_DISAGREE = 0,	// 거래를 신청받은 내가 거래 신청을 취소.. [Korean comment]
								PER_TRADE_RESULT_OTHER_DISAGREE,	// 거래를 신청받은 상대방이 거래 신청을 취소.. [Korean comment]
								PER_TRADE_RESULT_MY_CANCEL,			// 거래를 신청한 내가 거래 신청을 취소.. [Korean comment]


							};

enum e_PerTradeProceedCode	{										// 아이템 거래가 계속되는 상태를 정의.. Status
								PER_TRADE_RESULT_MY_AGREE = 0,		// 거래를 신청받은 내가 거래 신청을 허락.. [Korean comment]
								PER_TRADE_RESULT_OTHER_AGREE,		// 거래를 신청받은 상대방이 거래 신청을 허락.. [Korean comment]



							};

enum e_PerTradeItemKindBackup	{
									PER_TRADE_ITEM_MONEY = 0,		// 전에 개인 거래창으로 ADD한 것이 돈이다..
									PER_TRADE_ITEM_OTHER,			// 전에 개인 거래창으로 ADD한 것이 아이템이다..
								};

const uint32_t dwGold = 900000000;	// 음... [Korean comment]

class CUIManager;
class CUIPerTradeDlg;
class CUITradeEditDlg;

#include "GameBase.h"

class CSubProcPerTrade : public CGameBase
{
protected:
	int					m_iOtherID;
	int					m_iGoldOffsetBackup;

	std::string			m_szMsg;	// MessageBox key

public:
	CUIPerTradeDlg*				m_pUIPerTradeDlg;
	CUITradeEditDlg*			m_pUITradeEditDlg;

	e_PerTradeState				m_ePerTradeState;
	e_PerTradeItemKindBackup	m_ePerTradeItemKindBackup;

public:
	CSubProcPerTrade();
	virtual ~CSubProcPerTrade();

	void	Release();

	void	InitPerTradeDlg(CUIManager* pUIManager);

	void	EnterWaitMsgFromServerStatePerTradeReq();			// 내가 아이템 거래를 타인에게 신청한 상태.. Status
	void	EnterWaitMsgFromServerStatePerTradeReq(std::string szName);			// 내가 아이템 거래를 타인에게 신청한 상태.. Status
	void	EnterWaitMyDecisionToPerTrade(int iOtherID);		// 내가 타인에게서 아이템 거래를 신청 받은 상태.. Status
	void	LeavePerTradeState(e_PerTradeResultCode ePTRC);		// 아이템 거래 상태가 해제되는 코드.. Status
	void	ProcessProceed(e_PerTradeProceedCode ePTPC);		// 아이템 거래가 계속되는 상태를 정의.. Status

	void	SecureCodeBegin();									// 보호 코드.. [Korean comment]

	void	PerTradeCoreStart();
	void	PerTradeCoreInvDisable();

	void	RequestItemCountEdit();
	void	ItemCountEditOK();
	void	ItemCountEditCancel();

	void	FinalizePerTrade();									// 말 그대로 최종 뒷처리.. Process
	void	PerTradeCompleteSuccess();							// 개인 거래 최종 성공.. [Korean comment]
	void	PerTradeCompleteCancel();							// 개인 거래 취소..	 [Korean comment]

	void	PerTradeMyDecision();								// 내가 거래를 결정 했다.. [Korean comment]
	void	PerTradeOtherDecision();							// 다른 사람이 거래를 결정 했다.. [Korean comment]

	void	SecureJobStuffByMyDecision();

	void	ReceiveMsgPerTradeReq(int iOtherID);
	void	ReceiveMsgPerTradeAgree(uint8_t bResult);
	void	ReceiveMsgPerTradeAdd(uint8_t bResult);
	void	ReceiveMsgPerTradeOtherAdd(int iItemID, int iCount, int iDurability);
	void	ReceiveMsgPerTradeOtherDecide();
	void	ReceiveMsgPerTradeDoneSuccessBegin(int iTotalGold);
	void	ReceiveMsgPerTradeDoneItemMove(uint8_t bItemPos, int iItemID, int iCount, int iDurability);
	void	ReceiveMsgPerTradeDoneSuccessEnd();
	void	ReceiveMsgPerTradeDoneFail();
	void	ReceiveMsgPerTradeCancel();

	// Item Count OK..
	void	ItemCountOK();
	void	ItemCountCancel();
};

#endif // !defined(AFX_SUBPROCPERTRADE_H__555E4865_7877_425D_8F29_DD028F251889__INCLUDED_)
