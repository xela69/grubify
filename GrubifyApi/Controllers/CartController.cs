using Microsoft.AspNetCore.Mvc;
using GrubifyApi.Data;
using GrubifyApi.Models;
using GrubifyApi.Chaos;

namespace GrubifyApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CartController : ControllerBase
    {
        private readonly GrubifyDbContext _db;
        public CartController(GrubifyDbContext db) => _db = db;

        // In-memory cart storage (in production, use database)
        private static readonly Dictionary<string, Cart> UserCarts = new();

        // Only accumulates when the CHAOS_MEMORY_LEAK toggle is on (see AddItemToCart).
        // Bounded by CHAOS_MEMORY_LEAK_MAX_MB and guarded by a lock because the
        // controller is hit concurrently.
        private static readonly List<byte[]> RequestDataCache = new();
        private static readonly object RequestDataCacheLock = new();
        private static bool _leakCapLogged;
        private const int LeakChunkMb = 10;

        [HttpGet("{userId}")]
        public ActionResult<Cart> GetCart(string userId)
        {
            if (!UserCarts.ContainsKey(userId))
            {
                UserCarts[userId] = new Cart { UserId = userId };
            }
            return Ok(UserCarts[userId]);
        }

        [HttpPost("{userId}/items")]
        public ActionResult<Cart> AddItemToCart(string userId, [FromBody] AddCartItemRequest request)
        {
            // Chaos toggle (default OFF): leak 10MB per call to simulate a runaway cache,
            // capped so the fault can never take down the add-to-cart path with OOM.
            if (ChaosConfig.MemoryLeakEnabled)
            {
                LeakChaosMemory();
            }

            if (!UserCarts.ContainsKey(userId))
            {
                UserCarts[userId] = new Cart { UserId = userId };
            }

            var cart = UserCarts[userId];
            var existingItem = cart.Items.FirstOrDefault(i => i.FoodItemId == request.FoodItemId);

            if (existingItem != null)
            {
                existingItem.Quantity += request.Quantity;
                existingItem.SpecialInstructions = request.SpecialInstructions;
            }
            else
            {
                var newItem = new CartItem
                {
                    Id = cart.Items.Count + 1,
                    FoodItemId = request.FoodItemId,
                    FoodItem = GetFoodItemById(request.FoodItemId),
                    Quantity = request.Quantity,
                    SpecialInstructions = request.SpecialInstructions
                };
                cart.Items.Add(newItem);
            }

            return Ok(cart);
        }

        [HttpPut("{userId}/items/{itemId}")]
        public ActionResult<Cart> UpdateCartItem(string userId, int itemId, [FromBody] UpdateCartItemRequest request)
        {
            if (!UserCarts.ContainsKey(userId))
            {
                return NotFound("Cart not found");
            }

            var cart = UserCarts[userId];
            var item = cart.Items.FirstOrDefault(i => i.Id == itemId);

            if (item == null)
            {
                return NotFound("Item not found in cart");
            }

            item.Quantity = request.Quantity;
            item.SpecialInstructions = request.SpecialInstructions;

            return Ok(cart);
        }

        [HttpDelete("{userId}/items/{itemId}")]
        public ActionResult<Cart> RemoveItemFromCart(string userId, int itemId)
        {
            if (!UserCarts.ContainsKey(userId))
            {
                return NotFound("Cart not found");
            }

            var cart = UserCarts[userId];
            var item = cart.Items.FirstOrDefault(i => i.Id == itemId);

            if (item == null)
            {
                return NotFound("Item not found in cart");
            }

            cart.Items.Remove(item);
            return Ok(cart);
        }

        [HttpDelete("{userId}")]
        public ActionResult ClearCart(string userId)
        {
            if (UserCarts.ContainsKey(userId))
            {
                UserCarts[userId].Items.Clear();
            }
            return Ok();
        }

        private static void LeakChaosMemory()
        {
            var capMb = ChaosConfig.MemoryLeakMaxMb;
            lock (RequestDataCacheLock)
            {
                var currentMb = RequestDataCache.Count * LeakChunkMb;
                if (currentMb + LeakChunkMb > capMb)
                {
                    if (!_leakCapLogged)
                    {
                        _leakCapLogged = true;
                        Console.WriteLine($"[chaos] memory-leak: cap reached (~{currentMb}MB of {capMb}MB), no further allocation");
                    }
                    return;
                }

                RequestDataCache.Add(new byte[LeakChunkMb * 1024 * 1024]);
                Console.WriteLine($"[chaos] memory-leak: cache entries={RequestDataCache.Count} (~{RequestDataCache.Count * LeakChunkMb}MB, cap {capMb}MB)");
            }
        }

        // Resolves a food item from Azure SQL for cart line details.
        private FoodItem GetFoodItemById(int foodItemId)
        {
            return _db.FoodItems.Find(foodItemId) ?? new FoodItem();
        }
    }

    public class AddCartItemRequest
    {
        public int FoodItemId { get; set; }
        public int Quantity { get; set; }
        public string SpecialInstructions { get; set; } = string.Empty;
    }

    public class UpdateCartItemRequest
    {
        public int Quantity { get; set; }
        public string SpecialInstructions { get; set; } = string.Empty;
    }
}
