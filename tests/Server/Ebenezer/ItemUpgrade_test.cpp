#include <gtest/gtest.h>
#include "TestApp.h"
#include "TestUser.h"

#include "data/Item_test_data.h"
#include "data/ItemUpgrade_test_data.h"

#include <shared-server/utilities.h>

#include <cstdlib>
#include <memory>

using namespace Ebenezer;

class ItemUpgradeTest : public ::testing::Test
{
protected:
	static constexpr uint16_t ANVIL_NPC_ID = 10001;
	static constexpr uint8_t ZONE_ID       = 0;
	static constexpr uint16_t REGION_X     = 0;
	static constexpr uint16_t REGION_Z     = 0;

	std::unique_ptr<TestApp> _app;
	TestUser* _user = nullptr;

	void SetUp() override
	{
		_app = std::make_unique<TestApp>();
		EXPECT_TRUE(_app != nullptr);

		// Load required tables
		for (const auto& itemModel : s_itemData)
			EXPECT_TRUE(_app->AddItemEntry(itemModel));

		for (const auto& itemUpgradeModel : s_itemUpgradeData)
			EXPECT_TRUE(_app->AddItemUpgradeEntry(itemUpgradeModel));

		// Setup map
		auto map = _app->CreateMap(ZONE_ID);
		EXPECT_TRUE(map != nullptr);

		// Setup user
		_user = _app->AddUser();
		EXPECT_TRUE(_user != nullptr);

		// Mark player as ingame
		_user->SetState(CONNECTION_STATE_GAMESTART);

		// Add user to map
		EXPECT_TRUE(map->Add(_user, REGION_X, REGION_Z));

		// Setup anvil NPC
		auto anvilNpc = _app->CreateNPC(ANVIL_NPC_ID);
		EXPECT_TRUE(anvilNpc != nullptr);

		// Add NPC to map
		EXPECT_TRUE(map->Add(anvilNpc, REGION_X, REGION_Z));

		// Seed random number generator for consistent RNG lookups.
		srand(0);
	}

	void TearDown() override
	{
		_user = nullptr;
		_app.reset();
	}
};

TEST_F(ItemUpgradeTest, SampleTest)
{
	int sendIndex = 0;
	char sendBuffer[128] {};

	_ITEM_DATA& originItem      = _user->m_pUserData->m_sItemArray[SLOT_MAX + 0];
	_ITEM_DATA& reqItem1        = _user->m_pUserData->m_sItemArray[SLOT_MAX + 1];
	_ITEM_DATA& reqItem2        = _user->m_pUserData->m_sItemArray[SLOT_MAX + 2];

	// Dagger (+1)
	originItem                  = { .nNum = 110110001, .sCount = 1 };

	// Blessed Item Upgrade Scroll
	reqItem1                    = { .nNum = 379016000, .sCount = 1 };
	reqItem2                    = { .nNum = 0, .sCount = 0 };

	// Upgrades need gold
	_user->m_pUserData->m_iGold = 100'000'000;

	// NOTE: We should probably move this into its own method for repeat
	// usage but we'll still need to pass it invalid data for testing
	// otherwise, so this will be fine for the moment.
	sendIndex                   = 0;
	SetByte(sendBuffer, ITEM_UPGRADE_PROCESS, sendIndex);
	SetShort(sendBuffer, ANVIL_NPC_ID, sendIndex);
	SetDWORD(sendBuffer, originItem.nNum, sendIndex);
	SetByte(sendBuffer, 0, sendIndex);   /* origin position */
	SetDWORD(sendBuffer, reqItem1.nNum, sendIndex);
	SetByte(sendBuffer, 1, sendIndex);   /* reqItem1 position */
	SetDWORD(sendBuffer, reqItem2.nNum, sendIndex);
	SetByte(sendBuffer, 255, sendIndex); /* reqItem2 position - unused in this case */

	// Remaining unused items
	// This is all a complete mess that needs cleaning up but
	// it'll suffice for the moment as a proof of concept
	for (int i = 2; i < 10; i++)
	{
		SetDWORD(sendBuffer, 0, sendIndex);
		SetByte(sendBuffer, 255, sendIndex);
	}

	_user->ResetSend();

	// Expect the gold change packet
	_user->AddSendCallback(
		[](const char* pBuf, int len)
		{
			EXPECT_EQ(len, 10);
			EXPECT_EQ(pBuf[0], WIZ_GOLD_CHANGE);
			EXPECT_EQ(pBuf[1], GOLD_CHANGE_LOSE);
			// ... etc
		});

	// Then the success packet
	_user->AddSendCallback(
		[](const char* pBuf, int len)
		{
			EXPECT_EQ(len, 53);
			EXPECT_EQ(pBuf[0], WIZ_ITEM_UPGRADE);
			EXPECT_EQ(pBuf[1], ITEM_UPGRADE_PROCESS);
			EXPECT_EQ(pBuf[2], ITEM_UPGRADE_ERROR_SUCCEEDED);
			// ... etc
		});

	// Then the packet to show the visual effect for the anvil
	_user->AddSendCallback(
		[](const char* pBuf, int len)
		{
			EXPECT_EQ(len, 5);
			EXPECT_EQ(pBuf[0], WIZ_OBJECT_EVENT);
			EXPECT_EQ(pBuf[1], OBJECT_TYPE_ANVIL);
			// ... etc
		});

	_user->ItemUpgradeProcess(sendBuffer);
	EXPECT_EQ(_user->GetPacketsSent(), 3);
}

TEST_F(ItemUpgradeTest, ItemNotInInventory)
{
	int sendIndex = 0;
	char sendBuffer[128] {};

	_ITEM_DATA& reqItem1        = _user->m_pUserData->m_sItemArray[SLOT_MAX + 1];

	reqItem1                    = { .nNum = 379016000, .sCount = 1 };

	_user->m_pUserData->m_iGold = 100'000'000;

	sendIndex                   = 0;
	SetByte(sendBuffer, ITEM_UPGRADE_PROCESS, sendIndex);
	SetShort(sendBuffer, ANVIL_NPC_ID, sendIndex);
	SetDWORD(sendBuffer, 110110001, sendIndex);
	SetByte(sendBuffer, 0, sendIndex);
	SetDWORD(sendBuffer, reqItem1.nNum, sendIndex);
	SetByte(sendBuffer, 1, sendIndex);
	SetDWORD(sendBuffer, 0, sendIndex);
	SetByte(sendBuffer, 255, sendIndex);

	for (int i = 2; i < 10; i++)
	{
		SetDWORD(sendBuffer, 0, sendIndex);
		SetByte(sendBuffer, 255, sendIndex);
	}

	_user->ResetSend();

	// Expect only the error packet
	_user->AddSendCallback(
		[](const char* pBuf, int len)
		{
			EXPECT_EQ(len, 3);
			EXPECT_EQ(pBuf[0], WIZ_ITEM_UPGRADE);
			EXPECT_EQ(pBuf[1], ITEM_UPGRADE_PROCESS);
			EXPECT_EQ(pBuf[2], ITEM_UPGRADE_ERROR_NO_MATCH);
		});

	_user->ItemUpgradeProcess(sendBuffer);
	EXPECT_EQ(_user->GetPacketsSent(), 1);
}

TEST_F(ItemUpgradeTest, RequirementItemNotInInventory)
{
	int sendIndex = 0;
	char sendBuffer[128] {};

	_ITEM_DATA& originItem      = _user->m_pUserData->m_sItemArray[SLOT_MAX + 0];

	originItem                  = { .nNum = 110110001, .sCount = 1 }; // Dagger (+1)

	_user->m_pUserData->m_iGold = 100'000'000;

	sendIndex                   = 0;
	SetByte(sendBuffer, ITEM_UPGRADE_PROCESS, sendIndex);
	SetShort(sendBuffer, ANVIL_NPC_ID, sendIndex);
	SetDWORD(sendBuffer, originItem.nNum, sendIndex);
	SetByte(sendBuffer, 0, sendIndex);
	SetDWORD(sendBuffer, 379016000, sendIndex);
	SetByte(sendBuffer, 1, sendIndex);
	SetDWORD(sendBuffer, 0, sendIndex);
	SetByte(sendBuffer, 255, sendIndex);

	for (int i = 2; i < 10; i++)
	{
		SetDWORD(sendBuffer, 0, sendIndex);
		SetByte(sendBuffer, 255, sendIndex);
	}

	_user->ResetSend();

	_user->AddSendCallback(
		[](const char* pBuf, int len)
		{
			EXPECT_EQ(len, 3);
			EXPECT_EQ(pBuf[0], WIZ_ITEM_UPGRADE);
			EXPECT_EQ(pBuf[1], ITEM_UPGRADE_PROCESS);
			EXPECT_EQ(pBuf[2], ITEM_UPGRADE_ERROR_NO_MATCH);
		});

	_user->ItemUpgradeProcess(sendBuffer);
	EXPECT_EQ(_user->GetPacketsSent(), 1);
}

TEST_F(ItemUpgradeTest, InsufficientGold)
{
	int sendIndex = 0;
	char sendBuffer[128] {};

	_ITEM_DATA& originItem      = _user->m_pUserData->m_sItemArray[SLOT_MAX + 0];
	_ITEM_DATA& reqItem1        = _user->m_pUserData->m_sItemArray[SLOT_MAX + 1];
	_ITEM_DATA& reqItem2        = _user->m_pUserData->m_sItemArray[SLOT_MAX + 2];

	originItem                  = { .nNum = 110110001, .sCount = 1 }; // Dagger (+1)
	reqItem1                    = { .nNum = 379025000, .sCount = 1 }; // Blessed Elemental Scroll
	reqItem2                    = { .nNum = 0, .sCount = 0 };

	// Set gold to -100 - not enough for upgrade
	_user->m_pUserData->m_iGold = -100;

	sendIndex                   = 0;
	SetByte(sendBuffer, ITEM_UPGRADE_PROCESS, sendIndex);
	SetShort(sendBuffer, ANVIL_NPC_ID, sendIndex);
	SetDWORD(sendBuffer, originItem.nNum, sendIndex);
	SetByte(sendBuffer, 0, sendIndex);
	SetDWORD(sendBuffer, reqItem1.nNum, sendIndex);
	SetByte(sendBuffer, 1, sendIndex);
	SetDWORD(sendBuffer, reqItem2.nNum, sendIndex);
	SetByte(sendBuffer, 255, sendIndex);

	for (int i = 2; i < 10; i++)
	{
		SetDWORD(sendBuffer, 0, sendIndex);
		SetByte(sendBuffer, 255, sendIndex);
	}

	_user->ResetSend();

	_user->AddSendCallback(
		[](const char* pBuf, int len)
		{
			EXPECT_EQ(len, 3);
			EXPECT_EQ(pBuf[0], WIZ_ITEM_UPGRADE);
			EXPECT_EQ(pBuf[1], ITEM_UPGRADE_PROCESS);
			EXPECT_EQ(pBuf[2], ITEM_UPGRADE_ERROR_NEED_COINS);
		});

	_user->ItemUpgradeProcess(sendBuffer);
	EXPECT_EQ(_user->GetPacketsSent(), 1);
}

