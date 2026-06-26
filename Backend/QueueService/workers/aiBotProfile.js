"use strict";

const CHARACTER_MODELS = ["Ch18", "Ch36", "Ch45"];

const NICKNAME_PARTS = [
  "Viper", "Ghost", "Blaze", "Raptor", "Nova", "Cipher", "Rogue", "Falcon",
  "Storm", "Pixel", "Ace", "Rush", "Hawk", "Zero", "Lynx", "Bolt",
];

const NICKNAME_SUFFIXES = ["", "42", "99", "X", "Pro", "77", "13", ""];

const SHIRTS = ["tshirts_001", "tshirts_002", "tshirts_003", "tshirts_004"];
const PANTS = ["pants_001", "pants_002"];
const BOOTS = ["shoes_001"];
const GLOVES = ["gloves_001", "gloves_002", "gloves_003", "gloves_004", "gloves_005"];
const FACES = ["attachment_face_001", "attachment_face_002", "attachment_face_003"];
const HAIR = ["attachment_hair_001", "attachment_hair_002", "attachment_hair_003"];
const WEAPON_ASSAULT = ["weapon_ak47_000", "weapon_ak47_001", "weapon_ak47_002"];
const WEAPON_SNIPER = ["weapon_sniper_000", "weapon_sniper_001", "weapon_sniper_002"];
const WEAPON_PISTOL = ["weapon_pistol_000", "weapon_pistol_001", "weapon_pistol_002"];
const WEAPON_MP7 = ["weapon_mp7_000", "weapon_mp7_001", "weapon_mp7_002"];

function pick(list) {
  return list[Math.floor(Math.random() * list.length)];
}

function generateAiBotProfile() {
  const suffix = pick(NICKNAME_SUFFIXES);
  const base = pick(NICKNAME_PARTS);
  const nickname = suffix ? `${base}${suffix}` : base;

  return {
    nickname,
    characterModel: pick(CHARACTER_MODELS),
    skinShirt: pick(SHIRTS),
    skinPants: pick(PANTS),
    skinBoots: pick(BOOTS),
    skinGloves: pick(GLOVES),
    skinFace: pick(FACES),
    skinHair: pick(HAIR),
    skinWeaponAssault: pick(WEAPON_ASSAULT),
    skinWeaponSniper: pick(WEAPON_SNIPER),
    skinWeaponPistol: pick(WEAPON_PISTOL),
    skinWeaponMp7: pick(WEAPON_MP7),
    skill: 0.45 + Math.random() * 0.45,
    aggression: 0.35 + Math.random() * 0.5,
  };
}

function applyProfileToPlayer(player, profile, spawn) {
  if (!player || !profile) {
    return;
  }

  player.nickname = profile.nickname || player.nickname;
  player.characterModel = profile.characterModel || "Ch36";
  player.skinShirt = profile.skinShirt || "tshirts_001";
  player.skinPants = profile.skinPants || "pants_001";
  player.skinBoots = profile.skinBoots || "shoes_001";
  player.skinGloves = profile.skinGloves || "gloves_001";
  player.skinFace = profile.skinFace || "attachment_face_001";
  player.skinHair = profile.skinHair || "attachment_hair_003";
  player.skinWeaponAssault = profile.skinWeaponAssault || "weapon_ak47_000";
  player.skinWeaponSniper = profile.skinWeaponSniper || "weapon_sniper_000";
  player.skinWeaponPistol = profile.skinWeaponPistol || "weapon_pistol_000";
  player.skinWeaponMp7 = profile.skinWeaponMp7 || "weapon_mp7_000";

  if (spawn) {
    player.x = spawn.x;
    player.y = spawn.y;
    player.z = spawn.z;
    player.yaw = spawn.yaw || 0;
  }

  player.connected = true;
  player.hasPose = true;
  player.lastPoseMs = Date.now();
  player.animSpeed = 0;
  player.isSprinting = false;
  player.isAiming = false;
  player.lookPitch = 0;
}

module.exports = {
  generateAiBotProfile,
  applyProfileToPlayer,
};
