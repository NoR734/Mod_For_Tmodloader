using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace Hidingeffects.Common.Systems
{
	// Suppress dust and gore created by other players on your team (client-side only)
	public sealed class TeammateEffectsFilterSystem : ModSystem
	{
		private static int _suppressNestingDepth;
		
		// Store gore hook delegates so we can unsubscribe without referencing orig_* types
		private On_Gore.hook_NewGore _goreNewGoreHandler;
		private On_Gore.hook_NewGoreDirect _goreNewGoreDirectHandler;

		private static bool ShouldSuppress => _suppressNestingDepth > 0 && Main.netMode != 2; // never on server

		public override void Load() {
			if (Main.dedServ)
				return;

			// Bracket updates of other team players and their projectiles (and projectile death)
			On_Player.Update += On_Player_Update;
			On_Player.KillMe += On_Player_KillMe;
			On_Projectile.AI += On_Projectile_AI;
			On_Projectile.Kill += On_Projectile_Kill;

			// Intercept dust spawns
			On_Dust.NewDust += On_Dust_NewDust;
			On_Dust.NewDustDirect += On_Dust_NewDustDirect;
			On_Dust.NewDustPerfect += On_Dust_NewDustPerfect;

			// Intercept gore spawns (use lambdas to avoid version-specific orig_* types)
			_goreNewGoreHandler = (orig, source, position, velocity, type, scale) => {
				int idx = orig(source, position, velocity, type, scale);
				if (ShouldSuppress && idx >= 0 && idx < Main.maxGore) {
					Main.gore[idx].active = false;
				}
				return idx;
			};
			On_Gore.NewGore += _goreNewGoreHandler;

			_goreNewGoreDirectHandler = (orig, source, position, velocity, type, scale) => {
				Gore g = orig(source, position, velocity, type, scale);
				if (ShouldSuppress && g != null) {
					g.active = false;
				}
				return g;
			};
			On_Gore.NewGoreDirect += _goreNewGoreDirectHandler;
		}

		public override void Unload() {
			if (Main.dedServ)
				return;

			// Unsubscribe to avoid stale delegates on mod reload
			On_Player.Update -= On_Player_Update;
			On_Player.KillMe -= On_Player_KillMe;
			On_Projectile.AI -= On_Projectile_AI;
			On_Projectile.Kill -= On_Projectile_Kill;

			On_Dust.NewDust -= On_Dust_NewDust;
			On_Dust.NewDustDirect -= On_Dust_NewDustDirect;
			On_Dust.NewDustPerfect -= On_Dust_NewDustPerfect;

			if (_goreNewGoreHandler != null)
				On_Gore.NewGore -= _goreNewGoreHandler;
			if (_goreNewGoreDirectHandler != null)
				On_Gore.NewGoreDirect -= _goreNewGoreDirectHandler;
		}

		private static bool IsTeammatePlayer(int playerIndex) {
			if (playerIndex < 0 || playerIndex >= Main.maxPlayers)
				return false;

			Player p = Main.player[playerIndex];
			if (p is null || !p.active)
				return false;

			if (playerIndex == Main.myPlayer)
				return false; // never suppress local player's own effects

			int localTeam = Main.LocalPlayer?.team ?? 0;
			// Only treat as teammates when both are on the same non-zero team
			return localTeam != 0 && p.team == localTeam;
		}

		private static void EnterSuppression() => _suppressNestingDepth++;
		private static void ExitSuppression() {
			if (_suppressNestingDepth > 0)
				_suppressNestingDepth--;
		}

		// Player update bracketing
		private void On_Player_Update(On_Player.orig_Update orig, Player self, int i) {
			bool entered = IsTeammatePlayer(self.whoAmI);
			if (entered)
				EnterSuppression();
			try {
				orig(self, i);
			}
			finally {
				if (entered)
					ExitSuppression();
			}
		}

		private void On_Player_KillMe(On_Player.orig_KillMe orig, Player self, PlayerDeathReason damageSource, double dmg, int hitDirection, bool pvp) {
			bool entered = IsTeammatePlayer(self.whoAmI);
			if (entered)
				EnterSuppression();
			try {
				orig(self, damageSource, dmg, hitDirection, pvp);
			}
			finally {
				if (entered)
					ExitSuppression();
			}
		}

		// Projectile AI bracketing for projectiles owned by teammates
		private void On_Projectile_AI(On_Projectile.orig_AI orig, Projectile self) {
			bool entered = false;
			int owner = self.owner;
			if (owner >= 0 && owner < Main.maxPlayers && IsTeammatePlayer(owner)) {
				entered = true;
				EnterSuppression();
			}

			try {
				orig(self);
			}
			finally {
				if (entered)
					ExitSuppression();
			}
		}

		private void On_Projectile_Kill(On_Projectile.orig_Kill orig, Projectile self) {
			bool entered = false;
			int owner = self.owner;
			if (owner >= 0 && owner < Main.maxPlayers && IsTeammatePlayer(owner)) {
				entered = true;
				EnterSuppression();
			}
			try {
				orig(self);
			}
			finally {
				if (entered)
					ExitSuppression();
			}
		}

		// Dust creation interceptors
		private int On_Dust_NewDust(On_Dust.orig_NewDust orig,
			Vector2 position, int width, int height, int type,
			float speedX, float speedY, int alpha, Microsoft.Xna.Framework.Color newColor, float scale) {
			int idx = orig(position, width, height, type, speedX, speedY, alpha, newColor, scale);
			if (ShouldSuppress && idx >= 0 && idx < Main.maxDust) {
				Main.dust[idx].active = false;
			}
			return idx;
		}

		private Dust On_Dust_NewDustDirect(On_Dust.orig_NewDustDirect orig,
			Vector2 position, int width, int height, int type,
			float speedX, float speedY, int alpha, Microsoft.Xna.Framework.Color newColor, float scale) {
			Dust d = orig(position, width, height, type, speedX, speedY, alpha, newColor, scale);
			if (ShouldSuppress && d != null) {
				d.active = false;
			}
			return d;
		}

		private Dust On_Dust_NewDustPerfect(On_Dust.orig_NewDustPerfect orig,
			Vector2 position, int type, Vector2? speed, int alpha, Microsoft.Xna.Framework.Color? newColor, float scale) {
			Dust d = orig(position, type, speed, alpha, newColor, scale);
			if (ShouldSuppress && d != null) {
				d.active = false;
			}
			return d;
		}

		// Gore creation interceptors are attached in Load via stored delegates
	}
}
