#include <gtest/gtest.h>
#include "TestApp.h"
#include "TestUser.h"
#include "packet_structs.h"

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

TEST_F(ItemUpgradeTest, BasicUpgradeSucceeds)
{
	constexpr int OLD_ITEM_ID       = 110110001; // Dagger (+1)
	constexpr int NEW_ITEM_ID       = 110110002; // Dagger (+2)
	constexpr int REQ_ITEM1_ID      = 379016000; // Blessed Item Upgrade Scroll
	constexpr int START_GOLD        = 100'000'000;
	constexpr int EXPECTED_COST     = 0;
	constexpr int EXPECTED_NEW_GOLD = START_GOLD - EXPECTED_COST;

	char sendBuffer[128] {};
	int sendIndex = 0;

	ItemUpgradeProcessPacket packet {};

	_ITEM_DATA& originItem    = _user->m_pUserData->m_sItemArray[SLOT_MAX + 0];
	_ITEM_DATA& reqItem1      = _user->m_pUserData->m_sItemArray[SLOT_MAX + 1];

	model::Item* oldItemModel = _app->m_ItemTableMap.GetData(OLD_ITEM_ID);
	model::Item* newItemModel = _app->m_ItemTableMap.GetData(NEW_ITEM_ID);

	EXPECT_TRUE(oldItemModel != nullptr);
	EXPECT_TRUE(newItemModel != nullptr);

	// Prepare inventory
	originItem                  = { .nNum = OLD_ITEM_ID, .sDuration = 1, .sCount = 1 };
	reqItem1                    = { .nNum = REQ_ITEM1_ID, .sCount = 1 };

	// Upgrades need gold
	_user->m_pUserData->m_iGold = START_GOLD;

	// Prepare packet data
	packet.NpcID                = ANVIL_NPC_ID;
	packet.Item[0]              = { .ID = OLD_ITEM_ID, .Pos = 0 };
	packet.Item[1]              = { .ID = REQ_ITEM1_ID, .Pos = 1 };

	_user->ResetSend();

	// Expect the gold change packet
	_user->AddSendCallback(
		[=](const char* pBuf, int len)
		{
			EXPECT_EQ(len, sizeof(GoldChangePacket));

			auto packet = reinterpret_cast<const GoldChangePacket*>(pBuf);

			EXPECT_EQ(packet->Opcode, WIZ_GOLD_CHANGE);
			EXPECT_EQ(packet->SubOpcode, GOLD_CHANGE_LOSE);
			EXPECT_EQ(packet->ChangeAmount, EXPECTED_COST);
			EXPECT_EQ(packet->NewGold, EXPECTED_NEW_GOLD);
		});

	// Then the success packet
	_user->AddSendCallback(
		[=](const char* pBuf, int len)
		{
			EXPECT_EQ(len, sizeof(ItemUpgradeProcessResponseSuccessPacket));

			auto packet = reinterpret_cast<const ItemUpgradeProcessResponseSuccessPacket*>(
				pBuf);

			EXPECT_EQ(packet->Opcode, WIZ_ITEM_UPGRADE);
			EXPECT_EQ(packet->SubOpcode, ITEM_UPGRADE_PROCESS);
			EXPECT_EQ(packet->Result, ITEM_UPGRADE_ERROR_SUCCEEDED);
			EXPECT_EQ(packet->Item[0].ID, NEW_ITEM_ID);
			EXPECT_EQ(packet->Item[0].Pos, 0);
			EXPECT_EQ(packet->Item[1].ID, REQ_ITEM1_ID);
			EXPECT_EQ(packet->Item[1].Pos, 1);

			for (int i = 2; i < 10; i++)
			{
				EXPECT_EQ(packet->Item[i].ID, 0);
				EXPECT_EQ(packet->Item[i].Pos, -1);
			}
		});

	// Then the packet to show the visual effect for the anvil
	_user->AddSendCallback(
		[](const char* pBuf, int len)
		{
			EXPECT_EQ(len, sizeof(ObjectEventAnvilResponsePacket));

			auto packet = reinterpret_cast<const ObjectEventAnvilResponsePacket*>(pBuf);

			EXPECT_EQ(packet->Opcode, WIZ_OBJECT_EVENT);
			EXPECT_EQ(packet->ObjectType, OBJECT_TYPE_ANVIL);
			EXPECT_TRUE(packet->Successful);
			EXPECT_EQ(packet->NpcID, ANVIL_NPC_ID);
		});

	// Copy it into the larger buffer in case it were to ever read beyond the struct's size.
	sendIndex = 0;
	SetString(sendBuffer, reinterpret_cast<char*>(&packet), sizeof(packet), sendIndex);
	_user->ItemUpgradeProcess(sendBuffer);

	EXPECT_EQ(_user->GetPacketsSent(), 3);

	// Verify the item ID was updated in the inventory
	EXPECT_EQ(originItem.nNum, NEW_ITEM_ID);

	// Verify its durability was restored to max
	EXPECT_EQ(originItem.sDuration, newItemModel->Durability);
}

TEST_F(ItemUpgradeTest, BasicUpgradeBurns)
{
	constexpr int OLD_ITEM_ID       = 110110007; // Dagger (+7)
	constexpr int REQ_ITEM1_ID      = 379021000; // Blessed Upgrade Scroll (+0)
	constexpr int START_GOLD        = 100'000'000;
	constexpr int EXPECTED_COST     = 0;
	constexpr int EXPECTED_NEW_GOLD = START_GOLD - EXPECTED_COST;

	char sendBuffer[128] {};
	int sendIndex = 0;

	ItemUpgradeProcessPacket packet {};

	_ITEM_DATA& originItem    = _user->m_pUserData->m_sItemArray[SLOT_MAX + 0];
	_ITEM_DATA& reqItem1      = _user->m_pUserData->m_sItemArray[SLOT_MAX + 1];

	model::Item* oldItemModel = _app->m_ItemTableMap.GetData(OLD_ITEM_ID);
	EXPECT_TRUE(oldItemModel != nullptr);

	// Prepare inventory
	originItem                  = { .nNum = OLD_ITEM_ID, .sDuration = 1, .sCount = 1, .nSerialNum = 123456789 };
	reqItem1                    = { .nNum = REQ_ITEM1_ID, .sCount = 1 };

	// Upgrades need gold
	_user->m_pUserData->m_iGold = START_GOLD;

	// Prepare packet data
	packet.NpcID                = ANVIL_NPC_ID;
	packet.Item[0]              = { .ID = OLD_ITEM_ID, .Pos = 0 };
	packet.Item[1]              = { .ID = REQ_ITEM1_ID, .Pos = 1 };

	_user->ResetSend();

	// Expect the gold change packet
	_user->AddSendCallback(
		[=](const char* pBuf, int len)
		{
			EXPECT_EQ(len, sizeof(GoldChangePacket));

			auto packet = reinterpret_cast<const GoldChangePacket*>(pBuf);

			EXPECT_EQ(packet->Opcode, WIZ_GOLD_CHANGE);
			EXPECT_EQ(packet->SubOpcode, GOLD_CHANGE_LOSE);
			EXPECT_EQ(packet->ChangeAmount, EXPECTED_COST);
			EXPECT_EQ(packet->NewGold, EXPECTED_NEW_GOLD);
		});

	// Then the fail packet
	_user->AddSendCallback(
		[=](const char* pBuf, int len)
		{
			EXPECT_EQ(len, sizeof(ItemUpgradeProcessResponseSuccessPacket));

			auto packet = reinterpret_cast<const ItemUpgradeProcessResponseSuccessPacket*>(
				pBuf);

			EXPECT_EQ(packet->Opcode, WIZ_ITEM_UPGRADE);
			EXPECT_EQ(packet->SubOpcode, ITEM_UPGRADE_PROCESS);
			EXPECT_EQ(packet->Result, ITEM_UPGRADE_ERROR_FAILED);
			EXPECT_EQ(packet->Item[0].ID, OLD_ITEM_ID);
			EXPECT_EQ(packet->Item[0].Pos, 0);
			EXPECT_EQ(packet->Item[1].ID, REQ_ITEM1_ID);
			EXPECT_EQ(packet->Item[1].Pos, 1);

			for (int i = 2; i < 10; i++)
			{
				EXPECT_EQ(packet->Item[i].ID, 0);
				EXPECT_EQ(packet->Item[i].Pos, -1);
			}
		});

	// Then the packet to show the visual effect for the anvil
	_user->AddSendCallback(
		[](const char* pBuf, int len)
		{
			EXPECT_EQ(len, sizeof(ObjectEventAnvilResponsePacket));

			auto packet = reinterpret_cast<const ObjectEventAnvilResponsePacket*>(pBuf);

			EXPECT_EQ(packet->Opcode, WIZ_OBJECT_EVENT);
			EXPECT_EQ(packet->ObjectType, OBJECT_TYPE_ANVIL);
			EXPECT_FALSE(packet->Successful);
			EXPECT_EQ(packet->NpcID, ANVIL_NPC_ID);
		});

	// Copy it into the larger buffer in case it were to ever read beyond the struct's size.
	sendIndex = 0;
	SetString(sendBuffer, reinterpret_cast<char*>(&packet), sizeof(packet), sendIndex);
	_user->ItemUpgradeProcess(sendBuffer);

	EXPECT_EQ(_user->GetPacketsSent(), 3);

	// Verify the item was removed from the inventory
	EXPECT_EQ(originItem.nNum, 0);
	EXPECT_EQ(originItem.sDuration, 0);
	EXPECT_EQ(originItem.sCount, 0);
	EXPECT_EQ(originItem.nSerialNum, 0);
}

TEST_F(ItemUpgradeTest, OriginItemNotInInventory)
{
	constexpr int OLD_ITEM_ID  = 110110001; // Dagger (+1)
	constexpr int REQ_ITEM1_ID = 379016000; // Blessed Item Upgrade Scroll
	constexpr int START_GOLD   = 100'000'000;

	char sendBuffer[128] {};
	int sendIndex = 0;

	ItemUpgradeProcessPacket packet {};

	_ITEM_DATA& originItem      = _user->m_pUserData->m_sItemArray[SLOT_MAX + 0];
	_ITEM_DATA& reqItem1        = _user->m_pUserData->m_sItemArray[SLOT_MAX + 1];

	// Origin item purposefully doesn't exist in the inventory
	originItem                  = { .nNum = 0, .sCount = 0 };
	reqItem1                    = { .nNum = REQ_ITEM1_ID, .sCount = 1 };

	_user->m_pUserData->m_iGold = START_GOLD;

	// Prepare packet data
	packet.NpcID                = ANVIL_NPC_ID;
	packet.Item[0]              = { .ID = OLD_ITEM_ID, .Pos = 0 };
	packet.Item[1]              = { .ID = REQ_ITEM1_ID, .Pos = 1 };

	_user->ResetSend();

	// Expect only the error packet
	_user->AddSendCallback(
		[](const char* pBuf, int len)
		{
			EXPECT_EQ(len, sizeof(ItemUpgradeProcessErrorResponsePacket));

			auto packet = reinterpret_cast<const ItemUpgradeProcessErrorResponsePacket*>(pBuf);
			EXPECT_EQ(packet->Opcode, WIZ_ITEM_UPGRADE);
			EXPECT_EQ(packet->SubOpcode, ITEM_UPGRADE_PROCESS);
			EXPECT_EQ(packet->Result, ITEM_UPGRADE_ERROR_NO_MATCH);
		});

	// Copy it into the larger buffer in case it were to ever read beyond the struct's size.
	sendIndex = 0;
	SetString(sendBuffer, reinterpret_cast<char*>(&packet), sizeof(packet), sendIndex);
	_user->ItemUpgradeProcess(sendBuffer);

	EXPECT_EQ(_user->GetPacketsSent(), 1);
}

TEST_F(ItemUpgradeTest, RequirementItemNotInInventory)
{
	constexpr int OLD_ITEM_ID  = 110110001; // Dagger (+1)
	constexpr int REQ_ITEM1_ID = 379016000; // Blessed Item Upgrade Scroll
	constexpr int START_GOLD   = 100'000'000;

	char sendBuffer[128] {};
	int sendIndex = 0;

	ItemUpgradeProcessPacket packet {};

	_ITEM_DATA& originItem      = _user->m_pUserData->m_sItemArray[SLOT_MAX + 0];
	_ITEM_DATA& reqItem1        = _user->m_pUserData->m_sItemArray[SLOT_MAX + 1];

	// Requirement item purposefully doesn't exist in the inventory
	originItem                  = { .nNum = OLD_ITEM_ID, .sCount = 1 };
	reqItem1                    = { .nNum = 0, .sCount = 0 };

	_user->m_pUserData->m_iGold = START_GOLD;

	// Prepare packet data
	packet.NpcID                = ANVIL_NPC_ID;
	packet.Item[0]              = { .ID = OLD_ITEM_ID, .Pos = 0 };
	packet.Item[1]              = { .ID = REQ_ITEM1_ID, .Pos = 1 };

	_user->ResetSend();
	_user->AddSendCallback(
		[](const char* pBuf, int len)
		{
			EXPECT_EQ(len, sizeof(ItemUpgradeProcessErrorResponsePacket));

			auto packet = reinterpret_cast<const ItemUpgradeProcessErrorResponsePacket*>(pBuf);
			EXPECT_EQ(packet->Opcode, WIZ_ITEM_UPGRADE);
			EXPECT_EQ(packet->SubOpcode, ITEM_UPGRADE_PROCESS);
			EXPECT_EQ(packet->Result, ITEM_UPGRADE_ERROR_NO_MATCH);
		});

	// Copy it into the larger buffer in case it were to ever read beyond the struct's size.
	sendIndex = 0;
	SetString(sendBuffer, reinterpret_cast<char*>(&packet), sizeof(packet), sendIndex);
	_user->ItemUpgradeProcess(sendBuffer);

	EXPECT_EQ(_user->GetPacketsSent(), 1);
}

TEST_F(ItemUpgradeTest, InsufficientGold)
{
	constexpr int OLD_ITEM_ID  = 110110001; // Dagger (+1)
	constexpr int REQ_ITEM1_ID = 379025000; // Blessed Elemental Scroll
	constexpr int START_GOLD   = -100;      // -100 is not enough for an upgrade

	char sendBuffer[128] {};
	int sendIndex = 0;

	ItemUpgradeProcessPacket packet {};
	_ITEM_DATA& originItem      = _user->m_pUserData->m_sItemArray[SLOT_MAX + 0];
	_ITEM_DATA& reqItem1        = _user->m_pUserData->m_sItemArray[SLOT_MAX + 1];

	// Prepare inventory
	originItem                  = { .nNum = OLD_ITEM_ID, .sCount = 1 };
	reqItem1                    = { .nNum = REQ_ITEM1_ID, .sCount = 1 };

	// Set gold to -100 - not enough for upgrade
	_user->m_pUserData->m_iGold = START_GOLD;

	// Prepare packet data
	packet.NpcID                = ANVIL_NPC_ID;
	packet.Item[0]              = { .ID = OLD_ITEM_ID, .Pos = 0 };
	packet.Item[1]              = { .ID = REQ_ITEM1_ID, .Pos = 1 };

	_user->ResetSend();
	_user->AddSendCallback(
		[](const char* pBuf, int len)
		{
			EXPECT_EQ(len, sizeof(ItemUpgradeProcessErrorResponsePacket));

			auto packet = reinterpret_cast<const ItemUpgradeProcessErrorResponsePacket*>(pBuf);
			EXPECT_EQ(packet->Opcode, WIZ_ITEM_UPGRADE);
			EXPECT_EQ(packet->SubOpcode, ITEM_UPGRADE_PROCESS);
			EXPECT_EQ(packet->Result, ITEM_UPGRADE_ERROR_NEED_COINS);
		});

	// Copy it into the larger buffer in case it were to ever read beyond the struct's size.
	sendIndex = 0;
	SetString(sendBuffer, reinterpret_cast<char*>(&packet), sizeof(packet), sendIndex);
	_user->ItemUpgradeProcess(sendBuffer);

	EXPECT_EQ(_user->GetPacketsSent(), 1);
}
