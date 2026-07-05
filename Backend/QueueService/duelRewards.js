"use strict";

const MIN_RATING_DELTA = -27;
const MAX_RATING_DELTA = 27;
const MIN_COIN_REWARD = 0;
const MAX_COIN_REWARD = 155;
const DEFAULT_RATING = 1000;

function clamp(value, min, max) {
  return Math.max(min, Math.min(max, value));
}

function calculateDuelRewards(input) {
  const won = input && input.won === true;
  const roundWins = Math.max(0, Math.floor(Number(input && input.roundWins) || 0));
  const roundLosses = Math.max(0, Math.floor(Number(input && input.roundLosses) || 0));
  const playerRating = Math.max(0, Math.floor(Number(input && input.playerRating) || DEFAULT_RATING));
  const opponentRating = Math.max(0, Math.floor(Number(input && input.opponentRating) || DEFAULT_RATING));
  const damageDealt = Math.max(0, Math.floor(Number(input && input.damageDealt) || 0));
  const margin = roundWins - roundLosses;

  let rating = won ? 10 : -10;

  if (won) {
    rating += margin >= 2 ? 9 : 4;
  } else if (roundLosses >= 2 && roundWins === 0) {
    rating -= 5;
  } else if (roundWins >= 1) {
    rating += 6;
  }

  const expected = 1 / (1 + 10 ** ((playerRating - opponentRating) / 400));
  const score = won ? 1 : 0;
  rating += Math.round(10 * (score - expected));

  rating += won
    ? Math.min(5, Math.floor(damageDealt / 120))
    : Math.min(3, Math.floor(damageDealt / 180));

  const ratingDelta = clamp(Math.round(rating), MIN_RATING_DELTA, MAX_RATING_DELTA);

  let coins = 0;
  if (won) {
    coins = 55;
    coins += margin >= 2 ? 35 : 18;
    coins += roundWins * 12;
  } else {
    coins = 12;
    coins += roundWins * 22;
    if (roundLosses === 2 && roundWins === 1) {
      coins += 15;
    }
  }

  if (won && opponentRating > playerRating) {
    coins += Math.min(25, Math.floor((opponentRating - playerRating) / 40));
  } else if (!won && opponentRating < playerRating) {
    coins += Math.min(18, Math.floor((playerRating - opponentRating) / 50));
  }

  coins += Math.min(won ? 20 : 12, Math.floor(damageDealt / 55));

  const coinReward = clamp(Math.round(coins), MIN_COIN_REWARD, MAX_COIN_REWARD);

  return { ratingDelta, coinReward };
}

module.exports = {
  MIN_RATING_DELTA,
  MAX_RATING_DELTA,
  MIN_COIN_REWARD,
  MAX_COIN_REWARD,
  calculateDuelRewards,
};
