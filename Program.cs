using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;

namespace VismaResourceShortageManager
{
    // Enums for fixed values
    public enum RoomType
    {
        MeetingRoom,
        Kitchen,
        Bathroom
    }

    public enum CategoryType
    {
        Electronics,
        Food,
        Other
    }

    // Models
    public class ShortageRequest
    {
        public string Title { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public RoomType Room { get; set; }
        public CategoryType Category { get; set; }
        public int Priority { get; set; }
        public DateTime CreatedOn { get; set; }
        public Guid Id { get; set; } = Guid.NewGuid();

        public override string ToString()
        {
            return $"[{Priority}] {Title} - {Name} ({Room}, {Category}) - Created: {CreatedOn:yyyy-MM-dd}";
        }
    }

    public class User
    {
        public string Name { get; set; } = string.Empty;
        public bool IsAdministrator { get; set; }
    }

    // Data Access Layer
    public interface IDataRepository
    {
        List<ShortageRequest> GetAllRequests();
        void SaveRequest(ShortageRequest request);
        void DeleteRequest(Guid id);
        void UpdateRequest(ShortageRequest request);
        void SaveData();
    }

    public class JsonDataRepository : IDataRepository
    {
        private readonly string _filePath;
        private List<ShortageRequest> _requests;

        public JsonDataRepository(string filePath = "shortage_requests.json")
        {
            _filePath = filePath;
            LoadData();
        }

        public List<ShortageRequest> GetAllRequests()
        {
            return new List<ShortageRequest>(_requests);
        }

        public void SaveRequest(ShortageRequest request)
        {
            _requests.Add(request);
            SaveData();
        }

        public void DeleteRequest(Guid id)
        {
            _requests.RemoveAll(r => r.Id == id);
            SaveData();
        }

        public void UpdateRequest(ShortageRequest request)
        {
            var existingIndex = _requests.FindIndex(r => r.Id == request.Id);
            if (existingIndex >= 0)
            {
                _requests[existingIndex] = request;
                SaveData();
            }
        }

        public void SaveData()
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Converters = { new JsonStringEnumConverter() }
                };
                var json = JsonSerializer.Serialize(_requests, options);
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving data: {ex.Message}");
            }
        }

        private void LoadData()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);
                    var options = new JsonSerializerOptions
                    {
                        Converters = { new JsonStringEnumConverter() }
                    };
                    _requests = JsonSerializer.Deserialize<List<ShortageRequest>>(json, options) ?? new List<ShortageRequest>();
                }
                else
                {
                    _requests = new List<ShortageRequest>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading data: {ex.Message}");
                _requests = new List<ShortageRequest>();
            }
        }
    }

    // Services
    public interface IShortageService
    {
        bool CreateRequest(ShortageRequest request, User currentUser);
        bool DeleteRequest(Guid id, User currentUser);
        List<ShortageRequest> GetFilteredRequests(User currentUser, ShortageFilter? filter = null);
    }

    public class ShortageFilter
    {
        public string? Title { get; set; }
        public DateTime? CreatedFrom { get; set; }
        public DateTime? CreatedTo { get; set; }
        public CategoryType? Category { get; set; }
        public RoomType? Room { get; set; }
    }

    public class ShortageService : IShortageService
    {
        private readonly IDataRepository _repository;

        public ShortageService(IDataRepository repository)
        {
            _repository = repository;
        }

        public bool CreateRequest(ShortageRequest request, User currentUser)
        {
            var existingRequest = _repository.GetAllRequests()
                .FirstOrDefault(r => r.Title.Equals(request.Title, StringComparison.OrdinalIgnoreCase) &&
                                   r.Room == request.Room);

            if (existingRequest != null)
            {
                if (request.Priority > existingRequest.Priority)
                {
                    existingRequest.Priority = request.Priority;
                    existingRequest.Name = request.Name;
                    existingRequest.Category = request.Category;
                    existingRequest.CreatedOn = DateTime.Now;
                    _repository.UpdateRequest(existingRequest);
                    Console.WriteLine("Existing request updated with higher priority.");
                    return true;
                }
                else
                {
                    Console.WriteLine("Warning: A request with the same title and room already exists with equal or higher priority.");
                    return false;
                }
            }

            request.CreatedOn = DateTime.Now;
            _repository.SaveRequest(request);
            return true;
        }

        public bool DeleteRequest(Guid id, User currentUser)
        {
            var request = _repository.GetAllRequests().FirstOrDefault(r => r.Id == id);
            if (request == null)
            {
                Console.WriteLine("Request not found.");
                return false;
            }

            if (!currentUser.IsAdministrator && !request.Name.Equals(currentUser.Name, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("You can only delete requests you created or be an administrator.");
                return false;
            }

            _repository.DeleteRequest(id);
            return true;
        }

        public List<ShortageRequest> GetFilteredRequests(User currentUser, ShortageFilter? filter = null)
        {
            var requests = _repository.GetAllRequests();

            // Filter by user permissions
            if (!currentUser.IsAdministrator)
            {
                requests = requests.Where(r => r.Name.Equals(currentUser.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            // Apply filters
            if (filter != null)
            {
                if (!string.IsNullOrEmpty(filter.Title))
                {
                    requests = requests.Where(r => r.Title.Contains(filter.Title, StringComparison.OrdinalIgnoreCase)).ToList();
                }

                if (filter.CreatedFrom.HasValue)
                {
                    requests = requests.Where(r => r.CreatedOn.Date >= filter.CreatedFrom.Value.Date).ToList();
                }

                if (filter.CreatedTo.HasValue)
                {
                    requests = requests.Where(r => r.CreatedOn.Date <= filter.CreatedTo.Value.Date).ToList();
                }

                if (filter.Category.HasValue)
                {
                    requests = requests.Where(r => r.Category == filter.Category.Value).ToList();
                }

                if (filter.Room.HasValue)
                {
                    requests = requests.Where(r => r.Room == filter.Room.Value).ToList();
                }
            }

            // Sort by priority (highest first)
            return requests.OrderByDescending(r => r.Priority).ThenByDescending(r => r.CreatedOn).ToList();
        }
    }

    // Console UI
    public class ConsoleUI
    {
        private readonly IShortageService _shortageService;
        private User _currentUser;

        public ConsoleUI(IShortageService shortageService)
        {
            _shortageService = shortageService;
            _currentUser = new User();
        }

        public void Run()
        {
            InitializeUser();
            ShowMainMenu();
        }

        private void InitializeUser()
        {
            Console.WriteLine("=== Visma Resource Shortage Manager ===");
            Console.Write("Enter your name: ");
            _currentUser.Name = Console.ReadLine() ?? "Unknown";

            Console.Write("Are you an administrator? (y/n): ");
            var isAdmin = Console.ReadLine()?.ToLower() == "y";
            _currentUser.IsAdministrator = isAdmin;

            Console.WriteLine($"Welcome, {_currentUser.Name}!");
            Console.WriteLine();
        }

        private void ShowMainMenu()
        {
            while (true)
            {
                Console.WriteLine("=== Main Menu ===");
                Console.WriteLine("1. Register new shortage");
                Console.WriteLine("2. List requests");
                Console.WriteLine("3. Delete request");
                Console.WriteLine("4. Exit");
                Console.Write("Choose an option: ");

                var choice = Console.ReadLine();
                Console.WriteLine();

                switch (choice)
                {
                    case "1":
                        RegisterShortage();
                        break;
                    case "2":
                        ListRequests();
                        break;
                    case "3":
                        DeleteRequest();
                        break;
                    case "4":
                        return;
                    default:
                        Console.WriteLine("Invalid option. Please try again.");
                        break;
                }

                Console.WriteLine();
            }
        }

        private void RegisterShortage()
        {
            Console.WriteLine("=== Register New Shortage ===");

            var request = new ShortageRequest();

            Console.Write("Title: ");
            request.Title = Console.ReadLine() ?? "";

            request.Name = _currentUser.Name;

            Console.WriteLine("Room options:");
            ShowEnumOptions<RoomType>();
            request.Room = GetEnumInput<RoomType>("Room");

            Console.WriteLine("Category options:");
            ShowEnumOptions<CategoryType>();
            request.Category = GetEnumInput<CategoryType>("Category");

            Console.Write("Priority (1-10): ");
            int priority;
            while (!int.TryParse(Console.ReadLine(), out priority) || priority < 1 || priority > 10)
            {
                Console.Write("Please enter a valid priority (1-10): ");
            }
            request.Priority = priority;

            if (_shortageService.CreateRequest(request, _currentUser))
            {
                Console.WriteLine("Request created successfully!");
            }
        }

        private void ListRequests()
        {
            Console.WriteLine("=== List Requests ===");
            Console.WriteLine("Apply filters? (y/n): ");
            var useFilters = Console.ReadLine()?.ToLower() == "y";

            ShortageFilter? filter = null;
            if (useFilters)
            {
                filter = CreateFilter();
            }

            var requests = _shortageService.GetFilteredRequests(_currentUser, filter);

            if (requests.Count == 0)
            {
                Console.WriteLine("No requests found.");
                return;
            }

            Console.WriteLine($"Found {requests.Count} request(s):");
            Console.WriteLine("ID".PadRight(38) + "Request Details");
            Console.WriteLine(new string('-', 80));

            foreach (var request in requests)
            {
                Console.WriteLine($"{request.Id.ToString().PadRight(38)}{request}");
            }
        }

        private void DeleteRequest()
        {
            Console.WriteLine("=== Delete Request ===");
            Console.Write("Enter request ID: ");
            var idInput = Console.ReadLine();

            if (!Guid.TryParse(idInput, out Guid id))
            {
                Console.WriteLine("Invalid ID format.");
                return;
            }

            if (_shortageService.DeleteRequest(id, _currentUser))
            {
                Console.WriteLine("Request deleted successfully!");
            }
        }

        private ShortageFilter CreateFilter()
        {
            var filter = new ShortageFilter();

            Console.Write("Filter by title (or press Enter to skip): ");
            var title = Console.ReadLine();
            if (!string.IsNullOrEmpty(title))
                filter.Title = title;

            Console.Write("Filter from date (yyyy-MM-dd, or press Enter to skip): ");
            var fromDate = Console.ReadLine();
            if (DateTime.TryParseExact(fromDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime from))
                filter.CreatedFrom = from;

            Console.Write("Filter to date (yyyy-MM-dd, or press Enter to skip): ");
            var toDate = Console.ReadLine();
            if (DateTime.TryParseExact(toDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime to))
                filter.CreatedTo = to;

            Console.WriteLine("Category options:");
            ShowEnumOptions<CategoryType>();
            Console.Write("Choose a category (or press Enter to skip): ");
            var input = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(input))
            {
                if (int.TryParse(input, out int num) && num >= 1 && num <= Enum.GetValues(typeof(CategoryType)).Length)
                {
                    filter.Category = (CategoryType)(num - 1); // Adjust for zero-based enum
                }
                else
                {
                    Console.WriteLine("Invalid input. Skipping category filter.");
                }
            }

            // ROOM input
            Console.WriteLine("Room options:");
            ShowEnumOptions<RoomType>();
            Console.Write("Choose a room (or press Enter to skip): ");
            var roomInput = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(roomInput))
            {
                if (int.TryParse(roomInput, out int roomNum) && roomNum >= 1 && roomNum <= Enum.GetValues(typeof(RoomType)).Length)
                {
                    filter.Room = (RoomType)(roomNum - 1); // Adjust for zero-based enum
                }
                else
                {
                    Console.WriteLine("Invalid input. Skipping room filter.");
                }
            }

            return filter;
        }

        private void ShowEnumOptions<T>() where T : Enum
        {
            var values = Enum.GetValues(typeof(T));
            for (int i = 0; i < values.Length; i++)
            {
                Console.WriteLine($"{i + 1}. {values.GetValue(i)}");
            }
        }

        private T GetEnumInput<T>(string prompt) where T : Enum
        {
            var values = Enum.GetValues(typeof(T));

            while (true)
            {
                Console.Write($"{prompt} (1-{values.Length}): ");
                if (int.TryParse(Console.ReadLine(), out int choice) && choice >= 1 && choice <= values.Length)
                {
                    return (T)values.GetValue(choice - 1)!;
                }
                Console.WriteLine($"Please enter a number between 1 and {values.Length}.");
            }
        }
    }

    // Main Program
    public class Program
    {
        public static void Main(string[] args)
        {
            var repository = new JsonDataRepository();
            var service = new ShortageService(repository);
            var ui = new ConsoleUI(service);

            ui.Run();
        }
    }
}
