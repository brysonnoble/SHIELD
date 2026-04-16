#include "menus.h"

#include <fstream>
#include <functional>
#include <iostream>
#include <string>
#include <vector>

namespace Menus
{
	std::vector<MenuItem> mainMenu;
	std::vector<MenuItem> testList;
}

// Displays the main menu of STE.
void dispMainMenu()
{
	dispSplash();
	defineMenus();
	menuHandler("Main Menu", Menus::mainMenu);
}

// Defines all menu items and their corresponding actions.
void defineMenus() {
	Menus::mainMenu = {
		{"Run Tests", []() {menuHandler("Test List", Menus::testList);}},
		{"Settings", []() {}}
	};
	Menus::testList = {
		// build this list using tests in Test Scripts folder
	};
}

// Handles the user's menu selection and executes the corresponding action.
void menuHandler(const std::string& header, const std::vector<MenuItem>& items) {
	int choice = -1;
	while (true) {
		std::system("cls");
		dispSplash();

		std::cout << "\n--- " << header << " ---\n";
		for (size_t i = 0; i < items.size(); ++i) {
			std::cout << i + 1 << ". " << items[i].name << "\n";
		}
		std::cout << "0. Back/Quit\n";
		std::cout << "Enter choice: ";
		std::cin >> choice;

		if (choice == 0) {
			break;
		}

		if (choice > 0 && choice <= static_cast<int>(items.size())) {
			items[choice - 1].action();
		}
	}
}

// Displays the splash art of STE.
void dispSplash()
{
	std::ifstream splashFile("splashArt.txt");
	if (splashFile.is_open())
	{
		std::string line;
		while (getline(splashFile, line))
		{
			std::cout << line << std::endl;
		}
		splashFile.close();
	}
	else
	{
		std::cerr << "Unable to open splashArt.txt." << std::endl;
	}
}
