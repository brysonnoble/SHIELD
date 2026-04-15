#include <functional>
#include <string>
#include <vector>

#ifndef MENUS_H_INCLUDED
#define MENUS_H_INCLUDED

struct MenuItem
{
	std::string name;
	std::function<void()> action;
};

/* Displays the main menu of STE.
	Precondition:
		The main menu of STE is defined in the function defineMenus().

	Postcondition:
		The main menu of STE is displayed successfully.
*/
void dispMainMenu();

/* Defines all menu items and their corresponding actions.
	Precondition:
		The menu items and their corresponding actions are defined in the function.

	Postcondition:
		The menu items and their corresponding actions are defined successfully.
*/
void defineMenus();

/* Handles the user's menu selection and executes the corresponding action.
	Precondition:
		header is the title of the menu being displayed.
		items is a vector of MenuItem structs representing the menu options.

	Postcondition:
		The user's menu selection is handled and the corresponding action is executed successfully.
*/
void menuHandler(const std::string& header, const std::vector<MenuItem>& items);

/* Displays the splash art of STE.

	Precondition:
		The splash art of STE is stored in the file "splashArt.txt" in the same directory as the executable.

	Postcondition:
		The splash art of STE is displayed successfully.

*/
void dispSplash();

#endif
