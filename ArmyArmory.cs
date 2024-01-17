﻿using System;
using System.Collections.Generic;
using Bannerlord.DynamicTroop.Comparers;
using Bannerlord.DynamicTroop.Extensions;
using log4net.Core;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;
using ItemPriorityQueue = TaleWorlds.Library.PriorityQueue<TaleWorlds.Core.ItemObject, (int, int)>;

namespace Bannerlord.DynamicTroop;

public static class ArmyArmory {
	public static readonly ItemRoster Armory = new();

	private static ItemObject[]? _cachedThrownWeapons;

	public static void AddItemToArmory(ItemObject item, int count = 1) { _ = Armory.AddToCounts(item, count); }

	public static void ReturnEquipmentToArmoryFromAgents(IEnumerable<Agent> agents) {
		Global.Log("ReturnEquipmentToArmoryFromAgents", Colors.Green, Level.Debug);
		var count = 0;
		foreach (var agent in agents)
			if (agent.IsValid()) {
				Global.Log($"Returning equipment of agent {agent.Character.StringId}", Colors.Green, Level.Debug);

				Global.ProcessAgentEquipment(agent,
											 item => {
												 _ = Armory.AddToCounts(item, 1);
												 Global.Log($"equipment {item.StringId} returned",
															Colors.Green,
															Level.Debug);
												 count++;
											 });
			}

		Global.Log($"{count} equipment reclaimed", Colors.Green, Level.Debug);
	}

	public static void AssignEquipment(Equipment equipment) {
		foreach (var slot in Global.EquipmentSlots) {
			var element = equipment.GetEquipmentFromSlot(slot);

			// 使用模式匹配来检查条件，并反转if语句来减少嵌套
			if (element.IsEmpty || element.Item is null) continue;

			var itemToAssign = Armory.FirstOrDefaultQ(a => !a.IsEmpty                                                &&
														   a.EquipmentElement.Item.StringId == element.Item.StringId &&
														   a.Amount                         > 0);

			if (!itemToAssign.IsEmpty)
				_ = Armory.AddToCounts(itemToAssign.EquipmentElement, -1);
			else
				Global.Log($"Assigning Empty item {element.Item.StringId}", Colors.Red, Level.Warn);
		}
	}

	public static void SellExcessEquipmentForThrowingWeapons() {
		var value         = SellExcessEquipment();
		var originalValue = value;
		_cachedThrownWeapons ??= MBObjectManager.Instance.GetObjectTypeList<ItemObject>()
												?.WhereQ(item => item.IsThrowingWeaponCanBeAcquired())
												.ToArrayQ();
		var cnt = 0;
		while (value > 0) {
			var item = _cachedThrownWeapons.GetRandomElement();
			AddItemToArmory(item);
			value -= item.Value;
			cnt++;
		}

		InformationManager.DisplayMessage(new InformationMessage(LocalizedTexts
																	 .GetSoldExcessEquipmentForThrowingWeapons(originalValue -
																			 value,
																		 cnt),
																 Colors.Green));
	}

	public static int SellExcessEquipment() {
		var excessValue = 0;
		var playerParty = MobileParty.MainParty;
		if (playerParty?.MemberRoster?.GetTroopRoster() == null) return 0;

		var memberCnt = playerParty.MemberRoster.GetTroopRoster()
								   .WhereQ(element => element.Character is { IsHero: false })
								   .SumQ(element => element.Number);

		foreach (var equipmentAndThreshold in EveryoneCampaignBehavior.EquipmentAndThresholds) {
			var armorTotalCount = Armory.WhereQ(kv => kv.EquipmentElement.Item?.ItemType == equipmentAndThreshold.Key)
										.SumQ(kv => kv.Amount);
			var surplusCount = armorTotalCount - equipmentAndThreshold.Value(memberCnt);
			if (surplusCount <= 0) continue;

			var surplusCountCpy = surplusCount;

			// 创建优先级队列
			ItemPriorityQueue armorQueue = new(new ArmorComparer());
			foreach (var kv in Armory.WhereQ(kv => kv.EquipmentElement.Item?.ItemType == equipmentAndThreshold.Key))
				armorQueue.Enqueue(kv.EquipmentElement.Item,
								   ((int)kv.EquipmentElement.Item.Tier, kv.EquipmentElement.Item.Value));

			// 移除多余的装备
			while (surplusCount > 0 && armorQueue.Count > 0) {
				var lowestArmor   = armorQueue.Dequeue();
				var countToRemove = Math.Min(Armory.GetItemNumber(lowestArmor.Key), surplusCount);
				_            =  Armory.AddToCounts(lowestArmor.Key, -countToRemove); // 减少数量
				surplusCount -= countToRemove;
				excessValue  += countToRemove * lowestArmor.Key.Value;
			}

			Global.Debug($"Sold {surplusCountCpy - surplusCount}x{equipmentAndThreshold.Key} items from player's armory");
		}

		Global.Debug($"Sold {excessValue} denars worth of equipment from player's armory");
		return excessValue;
	}
}