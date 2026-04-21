#pragma once

namespace STE {

	using namespace System;
	using namespace System::ComponentModel;
	using namespace System::Collections;
	using namespace System::Windows::Forms;
	using namespace System::Data;
	using namespace System::Drawing;

	/// <summary>
	/// Summary for menus
	/// </summary>
	public ref class menus : public System::Windows::Forms::Form
	{
	public:
		menus(void)
		{
			InitializeComponent();
			//
			//TODO: Add the constructor code here
			//
		}

	protected:
		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		~menus()
		{
			if (components)
			{
				delete components;
			}
		}
	private: System::Windows::Forms::ToolStrip^ ToolStrip;
	private: System::Windows::Forms::ToolStripButton^ debugButton;
	private: System::Windows::Forms::ToolStripButton^ uploadButton;
	protected:

	protected:


	private: System::Windows::Forms::StatusStrip^ statusStrip1;
	private: System::Windows::Forms::ToolStripStatusLabel^ versionLabel;
	private: System::Windows::Forms::DataGridView^ testList;
	private: System::Windows::Forms::OpenFileDialog^ openFileDialog1;
	private: System::Windows::Forms::DataGridViewCheckBoxColumn^ Selected;
	private: System::Windows::Forms::DataGridViewTextBoxColumn^ TestName;
	private: System::Windows::Forms::DataGridViewTextBoxColumn^ Duration;
	private: System::Windows::Forms::DataGridViewTextBoxColumn^ Passes;
	private: System::Windows::Forms::DataGridViewTextBoxColumn^ Fails;
	private: System::Windows::Forms::DataGridViewTextBoxColumn^ Aborts;
	private: System::Windows::Forms::ToolStripButton^ runButton;




	private:
		/// <summary>
		/// Required designer variable.
		/// </summary>
		System::ComponentModel::Container ^components;

#pragma region Windows Form Designer generated code
		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		void InitializeComponent(void)
		{
			System::ComponentModel::ComponentResourceManager^ resources = (gcnew System::ComponentModel::ComponentResourceManager(menus::typeid));
			this->ToolStrip = (gcnew System::Windows::Forms::ToolStrip());
			this->uploadButton = (gcnew System::Windows::Forms::ToolStripButton());
			this->debugButton = (gcnew System::Windows::Forms::ToolStripButton());
			this->statusStrip1 = (gcnew System::Windows::Forms::StatusStrip());
			this->versionLabel = (gcnew System::Windows::Forms::ToolStripStatusLabel());
			this->testList = (gcnew System::Windows::Forms::DataGridView());
			this->Selected = (gcnew System::Windows::Forms::DataGridViewCheckBoxColumn());
			this->TestName = (gcnew System::Windows::Forms::DataGridViewTextBoxColumn());
			this->Duration = (gcnew System::Windows::Forms::DataGridViewTextBoxColumn());
			this->Passes = (gcnew System::Windows::Forms::DataGridViewTextBoxColumn());
			this->Fails = (gcnew System::Windows::Forms::DataGridViewTextBoxColumn());
			this->Aborts = (gcnew System::Windows::Forms::DataGridViewTextBoxColumn());
			this->openFileDialog1 = (gcnew System::Windows::Forms::OpenFileDialog());
			this->runButton = (gcnew System::Windows::Forms::ToolStripButton());
			this->ToolStrip->SuspendLayout();
			this->statusStrip1->SuspendLayout();
			(cli::safe_cast<System::ComponentModel::ISupportInitialize^>(this->testList))->BeginInit();
			this->SuspendLayout();
			// 
			// ToolStrip
			// 
			this->ToolStrip->Items->AddRange(gcnew cli::array< System::Windows::Forms::ToolStripItem^  >(3) {
				this->uploadButton, this->debugButton,
					this->runButton
			});
			this->ToolStrip->Location = System::Drawing::Point(0, 0);
			this->ToolStrip->Name = L"ToolStrip";
			this->ToolStrip->Size = System::Drawing::Size(934, 25);
			this->ToolStrip->TabIndex = 0;
			this->ToolStrip->Text = L"toolStrip";
			// 
			// uploadButton
			// 
			this->uploadButton->Image = (cli::safe_cast<System::Drawing::Image^>(resources->GetObject(L"uploadButton.Image")));
			this->uploadButton->ImageTransparentColor = System::Drawing::Color::Magenta;
			this->uploadButton->Name = L"uploadButton";
			this->uploadButton->Size = System::Drawing::Size(102, 22);
			this->uploadButton->Text = L"Upload Test(s)";
			this->uploadButton->Click += gcnew System::EventHandler(this, &menus::uploadButton_Click);
			// 
			// debugButton
			// 
			this->debugButton->Image = (cli::safe_cast<System::Drawing::Image^>(resources->GetObject(L"debugButton.Image")));
			this->debugButton->ImageTransparentColor = System::Drawing::Color::Magenta;
			this->debugButton->Name = L"debugButton";
			this->debugButton->Size = System::Drawing::Size(146, 22);
			this->debugButton->Text = L"Debug Selected Test(s)";
			// 
			// statusStrip1
			// 
			this->statusStrip1->Items->AddRange(gcnew cli::array< System::Windows::Forms::ToolStripItem^  >(1) { this->versionLabel });
			this->statusStrip1->Location = System::Drawing::Point(0, 539);
			this->statusStrip1->Name = L"statusStrip1";
			this->statusStrip1->Size = System::Drawing::Size(934, 22);
			this->statusStrip1->TabIndex = 1;
			this->statusStrip1->Text = L"statusStrip";
			// 
			// versionLabel
			// 
			this->versionLabel->Name = L"versionLabel";
			this->versionLabel->Size = System::Drawing::Size(59, 17);
			this->versionLabel->Text = L"STE v0.0.1";
			// 
			// testList
			// 
			this->testList->AllowUserToAddRows = false;
			this->testList->AllowUserToDeleteRows = false;
			this->testList->Anchor = static_cast<System::Windows::Forms::AnchorStyles>((((System::Windows::Forms::AnchorStyles::Top | System::Windows::Forms::AnchorStyles::Bottom)
				| System::Windows::Forms::AnchorStyles::Left)
				| System::Windows::Forms::AnchorStyles::Right));
			this->testList->AutoSizeColumnsMode = System::Windows::Forms::DataGridViewAutoSizeColumnsMode::Fill;
			this->testList->ColumnHeadersHeightSizeMode = System::Windows::Forms::DataGridViewColumnHeadersHeightSizeMode::AutoSize;
			this->testList->Columns->AddRange(gcnew cli::array< System::Windows::Forms::DataGridViewColumn^  >(6) {
				this->Selected, this->TestName,
					this->Duration, this->Passes, this->Fails, this->Aborts
			});
			this->testList->Location = System::Drawing::Point(0, 28);
			this->testList->Name = L"testList";
			this->testList->Size = System::Drawing::Size(934, 513);
			this->testList->TabIndex = 2;
			// 
			// Selected
			// 
			this->Selected->HeaderText = L"Selected";
			this->Selected->Name = L"Selected";
			// 
			// TestName
			// 
			this->TestName->HeaderText = L"Test Name";
			this->TestName->Name = L"TestName";
			this->TestName->ReadOnly = true;
			// 
			// Duration
			// 
			this->Duration->HeaderText = L"Test Duration (hh:mm:ss)";
			this->Duration->Name = L"Duration";
			this->Duration->ReadOnly = true;
			// 
			// Passes
			// 
			this->Passes->HeaderText = L"Passes";
			this->Passes->Name = L"Passes";
			this->Passes->ReadOnly = true;
			// 
			// Fails
			// 
			this->Fails->HeaderText = L"Fails";
			this->Fails->Name = L"Fails";
			this->Fails->ReadOnly = true;
			// 
			// Aborts
			// 
			this->Aborts->HeaderText = L"Aborts";
			this->Aborts->Name = L"Aborts";
			this->Aborts->ReadOnly = true;
			// 
			// openFileDialog1
			// 
			this->openFileDialog1->DefaultExt = L"vb";
			this->openFileDialog1->Filter = L"Visual Basic Files (*.vb)|*.vb|All Files (*.*)|*.*";
			this->openFileDialog1->Multiselect = true;
			this->openFileDialog1->Title = L"Select Test File(s)";
			// 
			// runButton
			// 
			this->runButton->Image = (cli::safe_cast<System::Drawing::Image^>(resources->GetObject(L"runButton.Image")));
			this->runButton->ImageTransparentColor = System::Drawing::Color::Magenta;
			this->runButton->Name = L"runButton";
			this->runButton->Size = System::Drawing::Size(132, 22);
			this->runButton->Text = L"Run Selected Test(s)";
			// 
			// menus
			// 
			this->AllowDrop = true;
			this->AutoScaleDimensions = System::Drawing::SizeF(6, 13);
			this->AutoScaleMode = System::Windows::Forms::AutoScaleMode::Font;
			this->ClientSize = System::Drawing::Size(934, 561);
			this->Controls->Add(this->testList);
			this->Controls->Add(this->statusStrip1);
			this->Controls->Add(this->ToolStrip);
			this->Icon = (cli::safe_cast<System::Drawing::Icon^>(resources->GetObject(L"$this.Icon")));
			this->Name = L"menus";
			this->Text = L"S.H.I.E.L.D. Test Environment";
			this->ToolStrip->ResumeLayout(false);
			this->ToolStrip->PerformLayout();
			this->statusStrip1->ResumeLayout(false);
			this->statusStrip1->PerformLayout();
			(cli::safe_cast<System::ComponentModel::ISupportInitialize^>(this->testList))->EndInit();
			this->ResumeLayout(false);
			this->PerformLayout();

		}
#pragma endregion

	private:
		System::Void uploadButton_Click(System::Object^ sender, System::EventArgs^ e)
		{
			if (this->openFileDialog1->ShowDialog() == System::Windows::Forms::DialogResult::OK)
			{
				cli::array<System::String^>^ fileNames = this->openFileDialog1->FileNames;

				for each (System::String^ fileName in fileNames)
				{
					// Extract just the filename without path
					System::String^ testName = System::IO::Path::GetFileNameWithoutExtension(fileName);

					// Add a new row to the DataGridView
					cli::array<System::Object^>^ row = gcnew cli::array<System::Object^>(6) 
					{ 
						false,				// Selected
						testName,			// Test Name
						L"00:00:00",		// Duration
						L"0",				// Passes
						L"0",				// Fails
						L"0"				// Aborts
					};
					this->testList->Rows->Add(row);
				}
			}
		}

	};

}
